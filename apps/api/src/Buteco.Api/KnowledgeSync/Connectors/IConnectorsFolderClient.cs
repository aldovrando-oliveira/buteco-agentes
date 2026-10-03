namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>
/// Valida uma pasta pela rota de descrição do <c>apps/connectors</c>
/// (<c>GET /connectors/providers/{provider}/folder?id=</c>), assinando
/// <c>service:api</c>. Nunca lança por falha do outro app: toda falha volta como
/// código (design.md da change criacao-base-sincronizada, D2 e D3).
/// </summary>
public interface IConnectorsFolderClient
{
    Task<ConnectorsFolderResult> DescribeFolderAsync(string provider, string folderId, CancellationToken cancellationToken);
}
