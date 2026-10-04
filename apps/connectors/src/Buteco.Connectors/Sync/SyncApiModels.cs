namespace Buteco.Connectors.Sync;

/// <summary>Uma base sincronizada, como <c>GET /sync/knowledge-bases</c> a devolve.</summary>
public sealed record SyncedKnowledgeBase(
    Guid Id,
    string Provider,
    string FolderId,
    string FolderName,
    string FolderUrl,
    bool IsActive);

/// <summary>Referência e marcador gravados de um documento (D9 da #102).</summary>
public sealed record SyncedDocumentRef(string ExternalRef, string ExternalVersion, Guid DocumentId);

/// <summary>O corpo do upsert (D8 da #102).</summary>
public sealed record SyncedDocumentUpsert(
    string ExternalRef,
    string ExternalVersion,
    string Title,
    string SourceType,
    string Content);

/// <summary>Um arquivo que não entrou na base, com o motivo como código.</summary>
public sealed record SyncIgnoredFile(string ExternalRef, string Name, string Code, string? Detail);

/// <summary>
/// O desfecho de um ciclo, no formato de <c>POST /sync/knowledge-bases/{id}/sync-results</c>
/// (D2 da #102): os campos do outro desfecho vão nulos, porque o <c>apps/api</c> os recusa.
/// </summary>
public sealed record SyncCycleResult(
    string Outcome,
    string? FolderName,
    string? FolderUrl,
    IReadOnlyList<SyncIgnoredFile>? IgnoredFiles,
    SyncCycleError? Error)
{
    public static SyncCycleResult Succeeded(string folderName, string folderUrl, IReadOnlyList<SyncIgnoredFile> ignoredFiles) =>
        new("Succeeded", folderName, folderUrl, ignoredFiles, null);

    public static SyncCycleResult Failed(string code, string? detail) =>
        new("Failed", null, null, null, new SyncCycleError(code, detail));
}

public sealed record SyncCycleError(string Code, string? Detail);

/// <summary>
/// O que uma chamada ao <c>apps/api</c> deu, já classificado (design.md da change
/// ciclo-de-sincronizacao, D4, D7, D8). Quem decide o alcance de cada um é o ciclo.
/// </summary>
public enum SyncApiStatus
{
    /// <summary><c>2xx</c> esperado.</summary>
    Ok,

    /// <summary><c>400</c> com <c>code</c>: recusa de conteúdo (#120), propriedade do arquivo.</summary>
    ContentRefused,

    /// <summary><c>404</c>, ou <c>409</c> de base <c>Manual</c>: a base não é mais sincronizável (D8).</summary>
    BaseGone,

    /// <summary><c>503</c>: outra escrita do mesmo documento venceu duas vezes (D10 da #102).</summary>
    Contention,

    /// <summary><c>401</c> ou <c>403</c>: a chave ou a tabela de subjects; alcança todas as bases.</summary>
    Rejected,

    /// <summary>Sem resposta: falha de rede ou limite de tempo; alcança todas as bases.</summary>
    Unreachable,

    /// <summary>Qualquer outra resposta, inclusive <c>400</c> sem <c>code</c> e <c>500</c> (#138).</summary>
    ContractError,
}

public sealed record SyncApiResponse<T>(SyncApiStatus Status, T? Value = default, string? Code = null, string? Detail = null);
