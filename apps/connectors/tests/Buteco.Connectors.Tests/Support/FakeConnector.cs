using Buteco.Connectors.Connectors;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// O conector falso (connector-plugin, "Conector falso só na composição de teste"):
/// implementa os dois contratos, com respostas configuradas pelo teste. Registrado só
/// por <see cref="ConnectorsFactory"/>.
/// </summary>
public sealed class FakeConnector : IFolderNavigator, IFolderContentSource
{
    public const string Key = "fake";

    public const string AccountEmail = "conta-falsa@conectores.test";

    public Func<string?, IReadOnlyList<FolderEntry>> Browse { get; set; } = _ => [];

    public Func<string, FolderDescription> Describe { get; set; } =
        id => new FolderDescription(id, "Pasta falsa", $"https://falso.test/{id}");

    public Task<IReadOnlyList<FolderEntry>> BrowseAsync(string? parentId, CancellationToken cancellationToken) =>
        Task.FromResult(Browse(parentId));

    public Task<FolderDescription> DescribeFolderAsync(string folderId, CancellationToken cancellationToken) =>
        Task.FromResult(Describe(folderId));

    public Task<RootListing> ListRootAsync(string folderId, CancellationToken cancellationToken) =>
        Task.FromResult(new RootListing([], []));

    public Task<string> GetMarkdownAsync(RootFile file, CancellationToken cancellationToken) =>
        Task.FromResult($"# {file.Name}");
}
