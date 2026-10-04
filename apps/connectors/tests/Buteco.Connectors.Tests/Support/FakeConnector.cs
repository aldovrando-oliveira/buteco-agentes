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

    /// <summary>
    /// A listagem da raiz, configurável pelo teste (delta de connector-plugin da change
    /// ciclo-de-sincronizacao). Lançar <see cref="ConnectorFailure"/> aqui é a listagem que
    /// falha, inclusive "na segunda página": o contrato é completo ou falha.
    /// </summary>
    public Func<string, RootListing> ListRoot { get; set; } = _ => new RootListing([], []);

    /// <summary>O markdown de cada arquivo; lançar <see cref="ConnectorFailure"/> é a falha daquele arquivo.</summary>
    public Func<RootFile, string> Markdown { get; set; } = file => $"# {file.Name}";

    /// <summary>
    /// Esperado antes de devolver a listagem, quando presente: é onde o teste segura um
    /// ciclo em curso (a barreira dos pedidos simultâneos de "Sincronizar agora").
    /// </summary>
    public Func<Task>? ListRootGate { get; set; }

    private int _listRootCalls;

    public int ListRootCalls => Volatile.Read(ref _listRootCalls);

    /// <summary>As referências cujo markdown foi pedido, na ordem.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> MarkdownRequests { get; } = new();

    public async Task<RootListing> ListRootAsync(string folderId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _listRootCalls);
        if (ListRootGate is not null)
        {
            await ListRootGate();
        }

        return ListRoot(folderId);
    }

    public Task<string> GetMarkdownAsync(RootFile file, CancellationToken cancellationToken)
    {
        MarkdownRequests.Enqueue(file.ExternalRef);
        return Task.FromResult(Markdown(file));
    }
}
