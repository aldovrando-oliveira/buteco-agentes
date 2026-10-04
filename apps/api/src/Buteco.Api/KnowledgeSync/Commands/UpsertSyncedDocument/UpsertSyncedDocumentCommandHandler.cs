using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeSync.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;

/// <summary>
/// Upsert de documento de base sincronizada por <c>ExternalRef</c> (design.md da
/// change catalogo-base-sincronizada, D3, D8 e D10). As regras de normalização,
/// indexação e histórico são as mesmas da escrita do operador: o mesmo
/// <see cref="KnowledgeContentProcessor"/>, as mesmas fábricas de evento, e
/// <see cref="KnowledgeDocument.ApplyExternalRevision"/> delegando a
/// <see cref="KnowledgeDocument.Update"/> quando há mudança.
/// </summary>
public sealed class UpsertSyncedDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    KnowledgeIndexingRequestDispatcher indexingDispatcher,
    ILogger<UpsertSyncedDocumentCommandHandler> logger)
    : ICommandHandler<UpsertSyncedDocumentCommand, UpsertSyncedDocumentResult>
{
    /// <summary>Nome do índice único parcial de <c>(KnowledgeBaseId, ExternalRef)</c>.</summary>
    private const string ExternalRefIndexName = "IX_knowledge_documents_KnowledgeBaseId_ExternalRef";

    /// <summary>
    /// Evento do log que registra a corrida recuperada (D10). Público para o teste de
    /// corrida contar quantas vezes o caminho de <c>UniqueViolation</c> rodou: corrida
    /// que nunca viu a corrida não prova nada.
    /// </summary>
    public static readonly EventId ConcurrentUpsertRecoveredEvent = new(1021, "ConcurrentSyncedDocumentUpsertRecovered");

    /// <summary>
    /// Evento do log que registra a atualização relida e reaplicada depois de uma
    /// <see cref="DbUpdateConcurrencyException"/> (D10, token de concorrência). Público
    /// pelo mesmo motivo de <see cref="ConcurrentUpsertRecoveredEvent"/>.
    /// </summary>
    public static readonly EventId ConcurrentUpdateRetriedEvent = new(1022, "ConcurrentSyncedDocumentUpdateRetried");

    public async ValueTask<UpsertSyncedDocumentResult> Handle(UpsertSyncedDocumentCommand command, CancellationToken cancellationToken)
    {
        // Base inativa aceita o upsert: desativar impede o uso pelo agente, não a
        // manutenção do conteúdo.
        var lookup = await dbContext.LookupSyncedKnowledgeBaseAsync(command.KnowledgeBaseId, cancellationToken);
        if (lookup != SyncedKnowledgeBaseLookup.Synced)
        {
            return UpsertSyncedDocumentResult.NotSynced(lookup);
        }

        // Validação antes de tocar a entidade: conteúdo inválido ou acima do teto
        // deixa o documento existente como estava, inclusive o marcador.
        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return UpsertSyncedDocumentResult.Invalid(content.Refusal!);
        }

        var extractedText = content.ExtractedText!;

        var existing = await FindAsync(command, cancellationToken);
        if (existing is not null)
        {
            return await ApplyToExistingAsync(existing, command, extractedText, cancellationToken);
        }

        var document = KnowledgeDocument.CreateSynced(
            command.KnowledgeBaseId, command.Title, command.SourceType, extractedText, command.ExternalRef, command.ExternalVersion);

        // Mesmo SaveChanges do documento (historico-documentos-base, D2), e o pedido de
        // indexação também (indexacao-sem-job-orfao, D1 e D4): se o documento foi
        // gravado, o pedido foi, e o ciclo seguinte não precisa reenviar nada.
        var indexingRequest = KnowledgeIndexingRequest.For(document);
        dbContext.KnowledgeDocuments.Add(document);
        dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Created(document, command.Author));
        dbContext.KnowledgeIndexingRequests.Add(indexingRequest);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsExternalRefUniqueViolation(exception))
        {
            // Outro upsert da mesma referência venceu entre a leitura e este
            // SaveChanges (D10). Detach das entidades Added desta tentativa
            // (documento, evento e pedido de indexação) e UMA releitura, não um laço: o Postgres só
            // libera a violação para quem perde depois que o vencedor fez commit,
            // então o vencedor já está gravado. É o idioma de ContactSessionResolver
            // (apps/inbox). O log é a única evidência observável de que a corrida
            // aconteceu, e é o que o teste de corrida conta.
            logger.LogInformation(
                ConcurrentUpsertRecoveredEvent,
                "Upsert concorrente de documento sincronizado: a base {KnowledgeBaseId} já tinha a referência {ExternalRef} gravada por outra requisição; aplicado como atualização.",
                command.KnowledgeBaseId,
                command.ExternalRef);
            DetachAddedEntities();

            var winner = await FindAsync(command, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Violação de unicidade de ExternalRef sem documento vencedor na releitura.", exception);

            return await ApplyToExistingAsync(winner, command, extractedText, cancellationToken);
        }
        catch (DbUpdateException exception) when (KnowledgeBaseWriteFailures.MayBeDeletedBase(exception))
        {
            // A base foi excluída entre a leitura e a gravação: a FK do evento ou a do
            // documento recusa a linha (exclusao-base-conhecimento, D6). 404, que a #105
            // lê como fim do ciclo. Com a base existindo, é contenção: o conector repete.
            return await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken)
                ? UpsertSyncedDocumentResult.NotSynced(SyncedKnowledgeBaseLookup.NotFound)
                : UpsertSyncedDocumentResult.ConcurrentWrite();
        }

        // Despacha DEPOIS do SaveChanges que deu certo, nunca antes. Não lança (D2).
        await indexingDispatcher.DispatchAfterWriteAsync([indexingRequest.Id], cancellationToken);

        return UpsertSyncedDocumentResult.Success(document.Id, SyncedDocumentUpsertOutcome.Created);
    }

    private Task<KnowledgeDocument?> FindAsync(UpsertSyncedDocumentCommand command, CancellationToken cancellationToken) =>
        // Comparação como veio (ordinal no banco, collation determinística): 'AbC' e
        // 'abc' são documentos diferentes.
        dbContext.KnowledgeDocuments.FirstOrDefaultAsync(
            document => document.KnowledgeBaseId == command.KnowledgeBaseId && document.ExternalRef == command.ExternalRef,
            cancellationToken);

    /// <summary>
    /// Atualização com <c>ContentRevision</c> como token de concorrência (D10). Se outra
    /// escrita gravou o documento entre a leitura e o <c>SaveChanges</c>, o
    /// <c>UPDATE</c> não acha a revisão lida e o EF lança
    /// <see cref="DbUpdateConcurrencyException"/>: o handler limpa o change tracker,
    /// relê e reaplica UMA vez, e o perdedor fica com a revisão seguinte à do
    /// vencedor, publicando a própria indexação. Uma segunda falha seguida não vira
    /// 500: responde que a escrita concorrente impediu a gravação e que ela pode ser
    /// repetida.
    ///
    /// <para>
    /// Desde a exclusão de base (exclusao-base-conhecimento, D6), toda falha — de
    /// concorrência, de FK para a base ou impasse — pergunta antes se a base ainda
    /// existe: sem ela, 404, nunca 503 nem 500.
    /// </para>
    /// </summary>
    private async Task<UpsertSyncedDocumentResult> ApplyToExistingAsync(
        KnowledgeDocument document, UpsertSyncedDocumentCommand command, string extractedText, CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyOnceAsync(document, command, extractedText, cancellationToken);
        }
        catch (DbUpdateException exception) when (KnowledgeBaseWriteFailures.MayBeDeletedBase(exception))
        {
            if (await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken))
            {
                return UpsertSyncedDocumentResult.NotSynced(SyncedKnowledgeBaseLookup.NotFound);
            }

            logger.LogInformation(
                ConcurrentUpdateRetriedEvent,
                "Atualização concorrente do documento sincronizado {ExternalRef} da base {KnowledgeBaseId}: relido e reaplicado.",
                command.ExternalRef,
                command.KnowledgeBaseId);
        }

        var current = await FindAsync(command, cancellationToken);
        if (current is null)
        {
            // Excluído entre as duas tentativas: não há o que reaplicar sem decidir
            // por conta própria recriar o documento. O conector repete — a menos que a
            // base inteira tenha sumido nesse meio-tempo.
            return await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken)
                ? UpsertSyncedDocumentResult.NotSynced(SyncedKnowledgeBaseLookup.NotFound)
                : UpsertSyncedDocumentResult.ConcurrentWrite();
        }

        try
        {
            return await ApplyOnceAsync(current, command, extractedText, cancellationToken);
        }
        catch (DbUpdateException exception) when (KnowledgeBaseWriteFailures.MayBeDeletedBase(exception))
        {
            return await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken)
                ? UpsertSyncedDocumentResult.NotSynced(SyncedKnowledgeBaseLookup.NotFound)
                : UpsertSyncedDocumentResult.ConcurrentWrite();
        }
    }

    private async Task<UpsertSyncedDocumentResult> ApplyOnceAsync(
        KnowledgeDocument document, UpsertSyncedDocumentCommand command, string extractedText, CancellationToken cancellationToken)
    {
        // Título, tipo de origem e texto iguais: só o marcador é gravado, sem
        // evento, sem indexação e sem tocar UpdatedAt (D3).
        var outcome = document.ApplyExternalRevision(command.Title, command.SourceType, extractedText, command.ExternalVersion);

        if (outcome.HasDocumentChange)
        {
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Updated(document, outcome, command.Author));
        }

        // O pedido de indexação viaja no MESMO SaveChanges que grava o externalVersion
        // novo (indexacao-sem-job-orfao, D4). Era a separação dos dois que deixava o
        // documento sincronizado sem indexação para sempre: o marcador gravado, a
        // publicação falhando, e o ciclo seguinte respondendo Unchanged. Unchanged não
        // grava pedido. A tentativa perdida para a concorrência também não: o
        // KnowledgeBaseIsGoneAsync do chamador limpa o ChangeTracker antes de reler.
        var indexingRequest = outcome.NeedsIndexing ? KnowledgeIndexingRequest.For(document) : null;
        if (indexingRequest is not null)
        {
            dbContext.KnowledgeIndexingRequests.Add(indexingRequest);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Despacha DEPOIS do SaveChanges que deu certo: a falha de concorrência
        // acontece antes daqui, e a tentativa perdida não despacha nada.
        if (indexingRequest is not null)
        {
            await indexingDispatcher.DispatchAfterWriteAsync([indexingRequest.Id], cancellationToken);
        }

        return UpsertSyncedDocumentResult.Success(
            document.Id,
            outcome.HasDocumentChange ? SyncedDocumentUpsertOutcome.Updated : SyncedDocumentUpsertOutcome.Unchanged);
    }

    private void DetachAddedEntities()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // Só a violação DESTE índice é corrida de upsert; qualquer outra sobe.
    private static bool IsExternalRefUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ExternalRefIndexName,
        };
}
