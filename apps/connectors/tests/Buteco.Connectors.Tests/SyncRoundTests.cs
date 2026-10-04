using System.Net;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Uma rodada sobre todas as bases (design.md da change ciclo-de-sincronizacao, D3, D5,
/// D8, D9 e D14): a ordem, o que interrompe a rodada e o que interrompe só uma base.
/// </summary>
public class SyncRoundTests
{
    private readonly FakeConnector _connector = new();
    private readonly FakeSyncApiHandler _api = new();
    private readonly RefusalMemory _memory = new();
    private readonly SyncInProgress _inProgress = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly List<string> _described = [];
    private readonly SyncRound _round;

    public SyncRoundTests()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFolderNavigator>(FakeConnector.Key, _connector);
        services.AddKeyedSingleton<IFolderContentSource>(FakeConnector.Key, _connector);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        var client = new SyncApiClient(new SingleClientFactory(new HttpClient(_api) { BaseAddress = new Uri("http://api.test") }));
        var cycle = new KnowledgeBaseSyncCycle(client, services.BuildServiceProvider(), _memory, loggerFactory.CreateLogger<KnowledgeBaseSyncCycle>());
        _round = new SyncRound(client, cycle, _inProgress, _memory, loggerFactory.CreateLogger<SyncRound>());
        _connector.Describe = folderId =>
        {
            lock (_described)
            {
                _described.Add(folderId);
            }

            return new FolderDescription(folderId, "Pasta", $"https://falso.test/{folderId}");
        };
    }

    private Task RunAsync() => _round.RunAsync(CancellationToken.None);

    [Fact]
    public async Task Bases_AreSyncedOneByOne_InTheOrderOfTheList_InactiveIncluded()
    {
        var first = _api.AddBase();
        var second = _api.AddBase(isActive: false);
        var third = _api.AddBase();

        await RunAsync();

        Assert.Equal([first.FolderId, second.FolderId, third.FolderId], _described);
        Assert.All([first, second, third], knowledgeBase => Assert.Single(_api.Results(knowledgeBase.Id)));
    }

    [Fact]
    public async Task BaseWith404_DoesNotInterruptTheOthers()
    {
        var first = _api.AddBase();
        var gone = _api.AddBase();
        var third = _api.AddBase();
        _api.Documents.Remove(gone.Id);

        await RunAsync();

        Assert.Empty(_api.Results(gone.Id));
        Assert.Empty(_api.Upserts(gone.Id));
        Assert.Single(_api.Results(first.Id));
        Assert.Single(_api.Results(third.Id));
    }

    [Fact]
    public async Task UnexpectedExceptionInOneBase_IsLoggedAndTheRoundGoesOn()
    {
        var broken = _api.AddBase();
        var next = _api.AddBase();
        _connector.ListRoot = folderId => folderId == broken.FolderId
            ? throw new InvalidOperationException("defeito inesperado")
            : new RootListing([], []);

        await RunAsync();

        Assert.Single(_api.Results(next.Id));
        Assert.Contains(_logs.Lines, line => line.StartsWith("Error", StringComparison.Ordinal) && line.Contains(broken.Id.ToString(), StringComparison.Ordinal));
        Assert.True(_inProgress.TryStart(broken.Id), "A base com exceção ficou marcada como em sincronização.");
    }

    [Fact]
    public async Task RateLimited_StopsTheRound_AndTheNextBaseIsNotDescribed()
    {
        var limited = _api.AddBase();
        var next = _api.AddBase();
        _connector.ListRoot = _ => throw new ConnectorFailure("rate-limited");

        await RunAsync();

        Assert.Equal([limited.FolderId], _described);
        Assert.Empty(_api.Results(next.Id));
    }

    [Fact]
    public async Task ApiUnreachable_StopsTheRound()
    {
        var first = _api.AddBase();
        var next = _api.AddBase();
        _api.Override = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/documents", StringComparison.Ordinal)
            ? throw new HttpRequestException("recusada")
            : null;

        await RunAsync();

        Assert.Equal([first.FolderId], _described);
        Assert.Empty(_api.Results(next.Id));
    }

    [Fact]
    public async Task ListOfBasesUnreachable_EndsTheRoundWithoutDescribingAnything()
    {
        _api.AddBase();
        _api.Override = (_, _) => throw new HttpRequestException("recusada");

        await RunAsync();

        Assert.Empty(_described);
    }

    [Fact]
    public async Task BaseAlreadySyncing_IsSkipped_AndStaysMarked()
    {
        var busy = _api.AddBase();
        var free = _api.AddBase();
        Assert.True(_inProgress.TryStart(busy.Id));

        await RunAsync();

        Assert.Equal([free.FolderId], _described);
        Assert.False(_inProgress.TryStart(busy.Id), "A rodada liberou o lock de um ciclo que não era dela.");
    }

    [Fact]
    public async Task BasesThatLeftTheList_AreForgottenAtTheEndOfACompleteRound()
    {
        var gone = Guid.NewGuid();
        _memory.Remember(gone, "ref-a", "v1", "too-large", null);
        var kept = _api.AddBase();
        _memory.Remember(kept.Id, "ref-a", "v1", "too-large", null);
        _connector.ListRoot = _ => new RootListing([new RootFile("ref-a", "a.md", "v1", "text/markdown")], []);

        await RunAsync();

        Assert.Equal(0, _memory.CountFor(gone));
        Assert.Equal(1, _memory.CountFor(kept.Id));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
