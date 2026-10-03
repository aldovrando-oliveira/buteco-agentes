namespace Buteco.Connectors.Connectors;

/// <summary>
/// Primeiro dos dois contratos de conector (design.md, D1 e "Árvore de pastas"):
/// navegar e descrever o local. Consumido pelas rotas de <c>/connectors</c>.
/// Registrado keyed, com a chave do provedor.
/// </summary>
/// <remarks>
/// Toda falha sai como <see cref="ConnectorFailure"/>, com código, nunca como frase.
/// </remarks>
public interface IFolderNavigator
{
    /// <summary>
    /// Sem <paramref name="parentId"/>, o nível de cima do que a conta enxerga; com
    /// ele, as subpastas daquela pasta, depois de ler a própria pasta.
    /// </summary>
    Task<IReadOnlyList<FolderEntry>> BrowseAsync(string? parentId, CancellationToken cancellationToken);

    /// <summary>Verifica o acesso à pasta e devolve nome e URL web.</summary>
    Task<FolderDescription> DescribeFolderAsync(string folderId, CancellationToken cancellationToken);
}
