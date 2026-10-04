namespace Buteco.Connectors.Sync;

/// <summary>
/// Os códigos da ligação com o <c>apps/api</c> (design.md da change
/// ciclo-de-sincronizacao, D9). O prefixo <c>sync-</c> diz que a falha é nessa ligação, e
/// não no provedor; <c>api-not-configured</c> já existe e é a Drive API desligada no
/// projeto do Google. Mesmo formato de <see cref="Connectors.ConnectorCodes"/>.
/// </summary>
public static class SyncCodes
{
    public const string KnowledgeBaseNotFound = "knowledge-base-not-found";
    public const string NotConfigured = "sync-not-configured";
    public const string ApiUnavailable = "sync-api-unavailable";
    public const string ApiError = "sync-api-error";
}
