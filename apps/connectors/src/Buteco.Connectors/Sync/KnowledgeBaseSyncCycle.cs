using System.Diagnostics;
using Buteco.Connectors.Connectors;

namespace Buteco.Connectors.Sync;

/// <summary>O que o ciclo de uma base pede à rodada.</summary>
public enum SyncCycleOutcome
{
    /// <summary>O ciclo terminou e gravou o desfecho (ou tentou, D4); a rodada segue.</summary>
    Completed,

    /// <summary><c>404</c>/<c>409</c> na base: nada gravado (D8); a rodada segue.</summary>
    BaseGone,

    /// <summary><c>rate-limited</c> (D5), ou o <c>apps/api</c> sem resposta ou recusando o token: a rodada para.</summary>
    StopRound,
}

/// <summary>
/// O ciclo de uma base (design.md da change ciclo-de-sincronizacao; spec
/// <c>knowledge-sync-cycle</c>): descrever a pasta, listar a raiz, ler as referências,
/// enviar o que mudou de marcador, excluir o que sumiu e gravar o desfecho.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que nunca acontece:</b> exclusão sem a pasta descrita, sem a listagem completa e
/// antes de os upserts terminarem (D6). Toda saída antecipada — falha de base, cota,
/// <c>apps/api</c> fora do ar — sai antes do passo de exclusão.
/// </para>
/// <para>
/// O alcance de cada falha é o da tabela da D4. Exceção que não é falha de conector nem
/// resposta classificada escapa daqui de propósito: quem chama roda cada base no próprio
/// <c>try/catch</c> (D8, convenção 4).
/// </para>
/// </remarks>
public sealed class KnowledgeBaseSyncCycle(
    ISyncApiClient api,
    IServiceProvider services,
    RefusalMemory memory,
    ILogger<KnowledgeBaseSyncCycle> logger)
{
    public const string SourceType = "markdown";

    public async Task<SyncCycleOutcome> RunAsync(SyncedKnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var counts = new Counts();
        var outcome = await RunCoreAsync(knowledgeBase, counts, cancellationToken);

        // Nada de nome de arquivo, conteúdo, token ou chave (D13).
        logger.LogInformation(
            "Ciclo da base {KnowledgeBaseId}: {Outcome} em {ElapsedMs} ms; criados {Created}, atualizados {Updated}, inalterados {Unchanged}, excluídos {Deleted}, ignorados {Ignored} (da memória de recusas {Recalled}), código {Code}.",
            knowledgeBase.Id, outcome, stopwatch.ElapsedMilliseconds,
            counts.Created, counts.Updated, counts.Unchanged, counts.Deleted, counts.Ignored, counts.Recalled, counts.Code ?? "-");
        return outcome;
    }

    private async Task<SyncCycleOutcome> RunCoreAsync(SyncedKnowledgeBase knowledgeBase, Counts counts, CancellationToken cancellationToken)
    {
        var navigator = services.GetKeyedService<IFolderNavigator>(knowledgeBase.Provider);
        var source = services.GetKeyedService<IFolderContentSource>(knowledgeBase.Provider);
        if (navigator is null || source is null)
        {
            return await FailAsync(knowledgeBase, ConnectorCodes.ProviderNotConfigured, null, counts, cancellationToken);
        }

        // 1. Descrever a pasta: a falha de acesso para tudo aqui, antes de listar (D6).
        FolderDescription folder;
        try
        {
            folder = await navigator.DescribeFolderAsync(knowledgeBase.FolderId, cancellationToken);
        }
        catch (ConnectorFailure failure)
        {
            return await FailFromConnectorAsync(knowledgeBase, failure, counts, cancellationToken);
        }

        // 2. Listar a raiz: completa ou falha (contrato de IFolderContentSource).
        RootListing listing;
        try
        {
            listing = await source.ListRootAsync(knowledgeBase.FolderId, cancellationToken);
        }
        catch (ConnectorFailure failure)
        {
            return await FailFromConnectorAsync(knowledgeBase, failure, counts, cancellationToken);
        }

        // 3. Referências e marcadores gravados no apps/api.
        var refs = await api.ListDocumentRefsAsync(knowledgeBase.Id, cancellationToken);
        if (refs.Status != SyncApiStatus.Ok)
        {
            return await FromApiFailureAsync(knowledgeBase, refs.Status, counts, cancellationToken);
        }

        var stored = refs.Value!.ToDictionary(reference => reference.ExternalRef, reference => reference.ExternalVersion, StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        listed.UnionWith(listing.Files.Select(file => file.ExternalRef));
        listed.UnionWith(listing.Ignored.Select(file => file.ExternalRef));

        // A listagem é completa: o que não está nela saiu da pasta, e sai da memória (D14).
        memory.KeepOnly(knowledgeBase.Id, listed);

        var ignored = listing.Ignored
            .Select(file => new SyncIgnoredFile(file.ExternalRef, file.Name, file.Code, file.Detail))
            .ToList();

        // 4. Baixar e enviar o que mudou de marcador.
        foreach (var file in listing.Files)
        {
            if (stored.TryGetValue(file.ExternalRef, out var storedVersion) && storedVersion == file.ExternalVersion)
            {
                continue;
            }

            if (memory.TryRecall(knowledgeBase.Id, file.ExternalRef, file.ExternalVersion, out var recalledCode, out var recalledDetail))
            {
                ignored.Add(new SyncIgnoredFile(file.ExternalRef, file.Name, recalledCode, recalledDetail));
                counts.Recalled++;
                continue;
            }

            memory.Forget(knowledgeBase.Id, file.ExternalRef);

            string markdown;
            try
            {
                markdown = await source.GetMarkdownAsync(file, cancellationToken);
            }
            catch (ConnectorFailure failure) when (!IsBaseLevelOnFile(failure.Code))
            {
                // Falha deste arquivo (D4): fica nos ignorados, o documento continua.
                ignored.Add(new SyncIgnoredFile(file.ExternalRef, file.Name, failure.Code, failure.Detail));
                continue;
            }
            catch (ConnectorFailure failure)
            {
                return await FailFromConnectorAsync(knowledgeBase, failure, counts, cancellationToken);
            }

            var upsert = await api.UpsertAsync(
                knowledgeBase.Id,
                new SyncedDocumentUpsert(file.ExternalRef, file.ExternalVersion, file.Name, SourceType, markdown),
                cancellationToken);

            switch (upsert.Status)
            {
                case SyncApiStatus.Ok:
                    counts.Count(upsert.Value);
                    break;

                case SyncApiStatus.ContentRefused:
                    // Propriedade do arquivo (#120): ignorado com o código, e lembrado se for
                    // determinístico (D14).
                    ignored.Add(new SyncIgnoredFile(file.ExternalRef, file.Name, upsert.Code!, upsert.Detail));
                    memory.Remember(knowledgeBase.Id, file.ExternalRef, file.ExternalVersion, upsert.Code!, upsert.Detail);
                    break;

                case SyncApiStatus.Contention:
                    // D7: sem repetir e sem ignorar; o marcador não mudou, e o próximo ciclo envia de novo.
                    logger.LogWarning("Upsert em contenção na base {KnowledgeBaseId}; fica para o próximo ciclo.", knowledgeBase.Id);
                    break;

                default:
                    return await FromApiFailureAsync(knowledgeBase, upsert.Status, counts, cancellationToken);
            }
        }

        // 5. Excluir o que sumiu da listagem inteira, só agora (D6).
        foreach (var externalRef in stored.Keys.Where(externalRef => !listed.Contains(externalRef)))
        {
            var delete = await api.DeleteAsync(knowledgeBase.Id, externalRef, cancellationToken);
            switch (delete.Status)
            {
                case SyncApiStatus.Ok:
                    counts.Deleted++;
                    break;

                case SyncApiStatus.Contention:
                    logger.LogWarning("Exclusão em contenção na base {KnowledgeBaseId}; fica para o próximo ciclo.", knowledgeBase.Id);
                    break;

                default:
                    return await FromApiFailureAsync(knowledgeBase, delete.Status, counts, cancellationToken);
            }
        }

        // 6. Gravar o desfecho.
        counts.Ignored = ignored.Count;
        return await RecordAsync(knowledgeBase, SyncCycleResult.Succeeded(folder.Name, folder.WebUrl, ignored), cancellationToken);
    }

    /// <summary>
    /// Ao obter o markdown, só a credencial e a API desligada são da base; o resto é
    /// daquele arquivo (D4). <c>rate-limited</c> é da rodada (D5).
    /// </summary>
    private static bool IsBaseLevelOnFile(string code) =>
        code is ConnectorCodes.RateLimited or ConnectorCodes.ProviderAuthFailed or ConnectorCodes.ApiNotConfigured;

    private async Task<SyncCycleOutcome> FailFromConnectorAsync(
        SyncedKnowledgeBase knowledgeBase, ConnectorFailure failure, Counts counts, CancellationToken cancellationToken)
    {
        var outcome = await FailAsync(knowledgeBase, failure.Code, failure.Detail, counts, cancellationToken);

        // Cota é da service account inteira: a próxima base daria o mesmo erro (D5).
        return failure.Code == ConnectorCodes.RateLimited && outcome == SyncCycleOutcome.Completed
            ? SyncCycleOutcome.StopRound
            : outcome;
    }

    private async Task<SyncCycleOutcome> FromApiFailureAsync(
        SyncedKnowledgeBase knowledgeBase, SyncApiStatus status, Counts counts, CancellationToken cancellationToken)
    {
        switch (status)
        {
            case SyncApiStatus.BaseGone:
                memory.ForgetBase(knowledgeBase.Id);
                return SyncCycleOutcome.BaseGone;

            case SyncApiStatus.Unreachable or SyncApiStatus.Rejected:
                // Alcança todas as bases, e não há como gravar (D4).
                counts.Code = status == SyncApiStatus.Unreachable ? SyncCodes.ApiUnavailable : SyncCodes.ApiError;
                logger.LogError(
                    "apps/api {Status} durante o ciclo da base {KnowledgeBaseId}: a rodada para sem gravar.", status, knowledgeBase.Id);
                return SyncCycleOutcome.StopRound;

            default:
                logger.LogError(
                    "apps/api respondeu fora do contrato ({Status}) no ciclo da base {KnowledgeBaseId}.", status, knowledgeBase.Id);
                return await FailAsync(knowledgeBase, SyncCodes.ApiError, null, counts, cancellationToken);
        }
    }

    private async Task<SyncCycleOutcome> FailAsync(
        SyncedKnowledgeBase knowledgeBase, string code, string? detail, Counts counts, CancellationToken cancellationToken)
    {
        counts.Code = code;
        return await RecordAsync(knowledgeBase, SyncCycleResult.Failed(code, detail), cancellationToken);
    }

    private async Task<SyncCycleOutcome> RecordAsync(SyncedKnowledgeBase knowledgeBase, SyncCycleResult result, CancellationToken cancellationToken)
    {
        var recorded = await api.RecordResultAsync(knowledgeBase.Id, result, cancellationToken);
        switch (recorded.Status)
        {
            case SyncApiStatus.Ok:
                return SyncCycleOutcome.Completed;

            case SyncApiStatus.BaseGone:
                memory.ForgetBase(knowledgeBase.Id);
                return SyncCycleOutcome.BaseGone;

            case SyncApiStatus.Unreachable or SyncApiStatus.Rejected:
                logger.LogError("apps/api {Status} ao gravar o desfecho da base {KnowledgeBaseId}: a rodada para.", recorded.Status, knowledgeBase.Id);
                return SyncCycleOutcome.StopRound;

            default:
                logger.LogError(
                    "apps/api respondeu fora do contrato ({Status}) ao gravar o desfecho da base {KnowledgeBaseId}.",
                    recorded.Status, knowledgeBase.Id);
                return SyncCycleOutcome.Completed;
        }
    }

    private sealed class Counts
    {
        public int Created { get; set; }

        public int Updated { get; set; }

        public int Unchanged { get; set; }

        public int Deleted { get; set; }

        public int Ignored { get; set; }

        public int Recalled { get; set; }

        public string? Code { get; set; }

        public void Count(string? outcome)
        {
            switch (outcome)
            {
                case "Created":
                    Created++;
                    break;
                case "Updated":
                    Updated++;
                    break;
                default:
                    Unchanged++;
                    break;
            }
        }
    }
}
