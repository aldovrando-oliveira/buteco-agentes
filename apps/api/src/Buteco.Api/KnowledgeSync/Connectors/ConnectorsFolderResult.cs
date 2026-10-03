namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>A pasta como o <c>apps/connectors</c> a descreveu.</summary>
public sealed record ConnectorsFolderDescription(string Id, string Name, string WebUrl);

/// <summary>
/// Resultado da validação de uma pasta: a pasta descrita, ou o status, o código e o
/// detalhe que a resposta do <c>apps/api</c> vai levar (design.md da change
/// criacao-base-sincronizada, D2 e D3).
/// </summary>
public sealed record ConnectorsFolderResult(
    ConnectorsFolderDescription? Folder,
    int FailureStatus,
    string? FailureCode,
    string? FailureDetail)
{
    public bool Succeeded => Folder is not null;

    public static ConnectorsFolderResult Success(ConnectorsFolderDescription folder) => new(folder, 0, null, null);

    public static ConnectorsFolderResult Failure(int status, string code, string? detail = null) => new(null, status, code, detail);
}
