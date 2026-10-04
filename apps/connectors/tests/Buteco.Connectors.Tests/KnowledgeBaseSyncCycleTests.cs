using System.Net;
using System.Text.Json;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O ciclo de uma base (spec <c>knowledge-sync-cycle</c>; design.md da change
/// ciclo-de-sincronizacao, D4 a D8 e D14), contra o <see cref="FakeConnector"/> e o
/// <see cref="FakeSyncApiHandler"/>. As asserções são sobre as requisições que chegaram ao
/// <c>apps/api</c> falso e sobre o markdown pedido ao conector, inclusive as negativas.
/// </summary>
public class KnowledgeBaseSyncCycleTests
{
    private const string DocMime = "application/vnd.google-apps.document";

    private readonly FakeConnector _connector = new();
    private readonly FakeSyncApiHandler _api = new();
    private readonly RefusalMemory _memory = new();
    private readonly KnowledgeBaseSyncCycle _cycle;
    private readonly SyncedKnowledgeBase _base;

    public KnowledgeBaseSyncCycleTests()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFolderNavigator>(FakeConnector.Key, _connector);
        services.AddKeyedSingleton<IFolderContentSource>(FakeConnector.Key, _connector);
        var client = new SyncApiClient(new SingleClientFactory(new HttpClient(_api) { BaseAddress = new Uri("http://api.test") }));
        _cycle = new KnowledgeBaseSyncCycle(client, services.BuildServiceProvider(), _memory, NullLogger<KnowledgeBaseSyncCycle>.Instance);
        _base = _api.AddBase();
        _connector.Describe = id => new FolderDescription(id, "Pasta descrita", $"https://falso.test/{id}");
    }

    private static RootFile File(string externalRef, string version = "v1", string? name = null) =>
        new(externalRef, name ?? $"{externalRef}.md", version, DocMime);

    private void Listing(params RootFile[] files) => _connector.ListRoot = _ => new RootListing(files, []);

    private Task<SyncCycleOutcome> RunAsync() => _cycle.RunAsync(_base, CancellationToken.None);

    private JsonElement SingleResult() => Assert.Single(_api.Results(_base.Id));

    private static List<(string Ref, string Code, string? Detail)> Ignored(JsonElement result) =>
        result.GetProperty("ignoredFiles").EnumerateArray()
            .Select(item => (
                item.GetProperty("externalRef").GetString()!,
                item.GetProperty("code").GetString()!,
                item.GetProperty("detail").ValueKind == JsonValueKind.Null ? null : item.GetProperty("detail").GetString()))
            .ToList();

    private void RefuseUpsertOf(string externalRef, Func<HttpResponseMessage> response) =>
        _api.Override = (request, body) =>
            request.Method == HttpMethod.Put && body!.Contains($"\"externalRef\":\"{externalRef}\"", StringComparison.Ordinal)
                ? response()
                : null;

    // --- Ordem do ciclo ------------------------------------------------------

    [Fact]
    public async Task SameMarker_IsNotDownloadedNorSent()
    {
        _api.AddDocument(_base.Id, "ref-a", "v1");
        Listing(File("ref-a", "v1"));

        Assert.Equal(SyncCycleOutcome.Completed, await RunAsync());

        Assert.Empty(_connector.MarkdownRequests);
        Assert.Empty(_api.Upserts(_base.Id));
        Assert.Equal("Succeeded", SingleResult().GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task NewMarkerAndNewFile_AreDownloadedAndSentWithTheFileNameAsTitle()
    {
        _api.AddDocument(_base.Id, "ref-a", "v1");
        Listing(File("ref-a", "v2", "Manual de trocas"), File("ref-b", "v1", "Política.md"));

        await RunAsync();

        Assert.Equal(["ref-a", "ref-b"], _connector.MarkdownRequests);
        var sent = _api.Upserts(_base.Id).Select(request => JsonDocument.Parse(request.Body!).RootElement).ToList();
        Assert.Equal(["Manual de trocas", "Política.md"], sent.Select(body => body.GetProperty("title").GetString()));
        Assert.All(sent, body => Assert.Equal("markdown", body.GetProperty("sourceType").GetString()));
        Assert.Equal(["v2", "v1"], sent.Select(body => body.GetProperty("externalVersion").GetString()));
    }

    [Fact]
    public async Task Success_RecordsTheDescribedFolderAndAnEmptyIgnoredList()
    {
        Listing(File("ref-a"));

        await RunAsync();

        var result = SingleResult();
        Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
        Assert.Equal("Pasta descrita", result.GetProperty("folderName").GetString());
        Assert.Equal($"https://falso.test/{_base.FolderId}", result.GetProperty("folderUrl").GetString());
        Assert.Empty(Ignored(result));
    }

    // --- Exclusão -------------------------------------------------------------

    [Fact]
    public async Task MissingFile_IsDeletedByReference_AndOnlyIt()
    {
        _api.AddDocument(_base.Id, "ref-a", "v1");
        _api.AddDocument(_base.Id, "ref-sumiu", "v1");
        Listing(File("ref-a", "v1"));

        await RunAsync();

        var delete = Assert.Single(_api.Deletes(_base.Id));
        Assert.EndsWith("externalRef=ref-sumiu", delete.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FolderAccessDenied_DeletesNothing_AndRecordsFailedWithTheEmail()
    {
        _api.AddDocument(_base.Id, "ref-a", "v1");
        _connector.Describe = _ => throw new ConnectorFailure("access-denied", FakeConnector.AccountEmail);

        Assert.Equal(SyncCycleOutcome.Completed, await RunAsync());

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Equal(0, _connector.ListRootCalls);
        var result = SingleResult();
        Assert.Equal("Failed", result.GetProperty("outcome").GetString());
        Assert.Equal("access-denied", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(FakeConnector.AccountEmail, result.GetProperty("error").GetProperty("detail").GetString());
    }

    // O contrato da listagem é "completa ou falha": a segunda página que falha chega
    // como ConnectorFailure da listagem inteira.
    [Fact]
    public async Task ListingThatFailsOnALaterPage_DeletesNothing_SendsNothing_AndIsNotSucceeded()
    {
        _api.AddDocument(_base.Id, "ref-a", "v1");
        _connector.ListRoot = _ => throw new ConnectorFailure("provider-unavailable");

        await RunAsync();

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Empty(_api.Upserts(_base.Id));
        var result = SingleResult();
        Assert.Equal("Failed", result.GetProperty("outcome").GetString());
        Assert.Equal("provider-unavailable", result.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task IgnoredFileStillInTheFolder_KeepsTheDocument()
    {
        _api.AddDocument(_base.Id, "ref-bloqueado", "v1");
        _api.AddDocument(_base.Id, "ref-grande", "v1");
        _connector.ListRoot = _ => new RootListing(
            [File("ref-grande", "v2")],
            [new IgnoredFile("ref-bloqueado", "Bloqueado", "download-blocked", null)]);
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 2_000_000));

        await RunAsync();

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Equal("v1", _api.Documents[_base.Id]["ref-bloqueado"].Version);
        Assert.Equal("v1", _api.Documents[_base.Id]["ref-grande"].Version);
        Assert.Equal(
            [("ref-bloqueado", "download-blocked", null), ("ref-grande", "too-large", "2000000")],
            Ignored(SingleResult()).OrderBy(item => item.Ref, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reference_IsComparedAsItCame()
    {
        _api.AddDocument(_base.Id, "AbC", "v1");
        Listing(File("abc", "v1"));

        await RunAsync();

        Assert.EndsWith("externalRef=AbC", Assert.Single(_api.Deletes(_base.Id)).PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("\"externalRef\":\"abc\"", Assert.Single(_api.Upserts(_base.Id)).Body, StringComparison.Ordinal);
    }

    // --- Falha de arquivo × falha de base -------------------------------------

    [Fact]
    public async Task ContentRefusals_BecomeIgnoredFiles_WithoutBreakingTheCycle()
    {
        _api.AddDocument(_base.Id, "ref-grande", "v1");
        Listing(File("ref-grande", "v2"), File("ref-vazio"), File("ref-ok"));
        _api.Override = (request, body) => request.Method != HttpMethod.Put
            ? null
            : body!.Contains("ref-grande", StringComparison.Ordinal) ? FakeSyncApiHandler.ContentRefusal("too-large", 1048577)
            : body.Contains("ref-vazio", StringComparison.Ordinal) ? FakeSyncApiHandler.ContentRefusal("empty-content")
            : null;

        await RunAsync();

        var result = SingleResult();
        Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
        Assert.Equal(
            [("ref-grande", "too-large", "1048577"), ("ref-vazio", "empty-content", null)],
            Ignored(result).OrderBy(item => item.Ref, StringComparer.Ordinal));
        Assert.Equal("v1", _api.Documents[_base.Id]["ref-grande"].Version);
        Assert.True(_api.Documents[_base.Id].ContainsKey("ref-ok"));
    }

    [Fact]
    public async Task ListingIgnoredFiles_GoToTheIgnoredListWithTheConnectorCode()
    {
        _connector.ListRoot = _ => new RootListing([], [
            new IgnoredFile("ref-atalho", "Atalho", "shortcut-not-followed", null),
            new IgnoredFile("ref-sub", "Subpasta", "subfolder-not-synced", null),
            new IgnoredFile("ref-planilha", "Planilha", "unsupported-type", "application/vnd.google-apps.spreadsheet"),
            new IgnoredFile("ref-bloqueado", "Bloqueado", "download-blocked", null),
        ]);

        await RunAsync();

        Assert.Equal(
            [
                ("ref-atalho", "shortcut-not-followed", null),
                ("ref-sub", "subfolder-not-synced", null),
                ("ref-planilha", "unsupported-type", "application/vnd.google-apps.spreadsheet"),
                ("ref-bloqueado", "download-blocked", null),
            ],
            Ignored(SingleResult()));
    }

    [Theory]
    [InlineData("file-not-found")]
    [InlineData("download-blocked")]
    [InlineData("provider-error")]
    [InlineData("provider-unavailable")]
    public async Task MarkdownFailureOfOneFile_DoesNotBreakTheBase(string code)
    {
        Listing(File("ref-falha"), File("ref-ok"));
        _connector.Markdown = file => file.ExternalRef == "ref-falha" ? throw new ConnectorFailure(code) : "# Ok\n";

        await RunAsync();

        var result = SingleResult();
        Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
        Assert.Equal([("ref-falha", code, null)], Ignored(result));
        Assert.Contains("ref-ok", Assert.Single(_api.Upserts(_base.Id)).Body, StringComparison.Ordinal);
    }

    // A credencial vale para todos os arquivos: falhar num é falhar a base (D4).
    [Fact]
    public async Task ProviderAuthFailedOnAFile_FailsTheBase_AndDeletesNothing()
    {
        _api.AddDocument(_base.Id, "ref-sumiu", "v1");
        Listing(File("ref-a"));
        _connector.Markdown = _ => throw new ConnectorFailure("provider-auth-failed");

        await RunAsync();

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Equal("provider-auth-failed", SingleResult().GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ShapeRefusal_IsNotIgnored_DeletesNothing_AndFailsWithSyncApiError()
    {
        _api.AddDocument(_base.Id, "ref-sumiu", "v1");
        Listing(File("ref-a"));
        RefuseUpsertOf("ref-a", FakeSyncApiHandler.ShapeRefusal);

        await RunAsync();

        Assert.Empty(_api.Deletes(_base.Id));
        var result = SingleResult();
        Assert.Equal("Failed", result.GetProperty("outcome").GetString());
        Assert.Equal("sync-api-error", result.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("ignoredFiles").ValueKind);
    }

    // O 500 da publicação da indexação com o documento já gravado (#138) é o mesmo caso.
    [Fact]
    public async Task ServerErrorOnUpsert_FailsWithSyncApiError()
    {
        Listing(File("ref-a"));
        RefuseUpsertOf("ref-a", () => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await RunAsync();

        Assert.Equal("sync-api-error", SingleResult().GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ProviderNotRegistered_FailsWithProviderNotConfigured()
    {
        var onedrive = _api.AddBase(provider: "onedrive");

        await _cycle.RunAsync(onedrive, CancellationToken.None);

        var result = Assert.Single(_api.Results(onedrive.Id));
        Assert.Equal("provider-not-configured", result.GetProperty("error").GetProperty("code").GetString());
    }

    // --- Cota -----------------------------------------------------------------

    [Fact]
    public async Task RateLimitedInTheMiddleOfTheBase_DeletesNothing_RecordsFailed_AndStopsTheRound()
    {
        _api.AddDocument(_base.Id, "ref-sumiu", "v1");
        Listing(File("ref-1"), File("ref-2"), File("ref-3"));
        _connector.Markdown = file => file.ExternalRef == "ref-2" ? throw new ConnectorFailure("rate-limited") : "# x\n";

        Assert.Equal(SyncCycleOutcome.StopRound, await RunAsync());

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Equal(["ref-1", "ref-2"], _connector.MarkdownRequests);
        var result = SingleResult();
        Assert.Equal("Failed", result.GetProperty("outcome").GetString());
        Assert.Equal("rate-limited", result.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task RateLimitedOnTheFolder_RecordsFailed_AndStopsTheRound()
    {
        _connector.Describe = _ => throw new ConnectorFailure("rate-limited");

        Assert.Equal(SyncCycleOutcome.StopRound, await RunAsync());

        Assert.Equal("rate-limited", SingleResult().GetProperty("error").GetProperty("code").GetString());
    }

    // --- Base inexistente e apps/api fora do ar -------------------------------

    [Fact]
    public async Task BaseNotFoundOnTheRefs_EndsTheBase_WithoutAnyOtherCallNorResult()
    {
        Listing(File("ref-a"));
        _api.Documents.Remove(_base.Id);

        Assert.Equal(SyncCycleOutcome.BaseGone, await RunAsync());

        Assert.Empty(_connector.MarkdownRequests);
        Assert.Empty(_api.Upserts(_base.Id));
        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Empty(_api.RequestsTo("POST", "/sync-results"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiUnreachableOrRejecting_StopsTheRound_WithoutDeletingNorRecording(bool unreachable)
    {
        _api.AddDocument(_base.Id, "ref-sumiu", "v1");
        Listing(File("ref-a"));
        _api.Override = (request, _) => request.Method == HttpMethod.Get
            ? unreachable ? throw new HttpRequestException("recusada") : new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : null;

        Assert.Equal(SyncCycleOutcome.StopRound, await RunAsync());

        Assert.Empty(_api.Deletes(_base.Id));
        Assert.Empty(_api.RequestsTo("POST", "/sync-results"));
    }

    // --- Contenção ------------------------------------------------------------

    [Fact]
    public async Task ContentionOnUpsert_IsNotRetriedNorIgnored_AndTheBaseSucceeds()
    {
        Listing(File("ref-a"), File("ref-b"));
        RefuseUpsertOf("ref-a", FakeSyncApiHandler.Contention);

        await RunAsync();

        var result = SingleResult();
        Assert.Equal("Succeeded", result.GetProperty("outcome").GetString());
        Assert.Empty(Ignored(result));
        Assert.Single(_api.Upserts(_base.Id), request => request.Body!.Contains("ref-a", StringComparison.Ordinal));
    }

    // --- Memória de recusas determinísticas (D14) -----------------------------

    [Fact]
    public async Task TooLarge_SameMarkerNextCycle_IsNotDownloadedNorSent_AndStaysIgnored()
    {
        Listing(File("ref-grande", "v1"));
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 1048577));
        await RunAsync();
        var requestsBefore = _connector.MarkdownRequests.Count;
        var upsertsBefore = _api.Upserts(_base.Id).Count;

        await RunAsync();

        Assert.Equal(1, requestsBefore);
        Assert.Equal(requestsBefore, _connector.MarkdownRequests.Count);
        Assert.Equal(upsertsBefore, _api.Upserts(_base.Id).Count);
        var second = _api.Results(_base.Id)[1];
        Assert.Equal([("ref-grande", "too-large", "1048577")], Ignored(second));
    }

    [Fact]
    public async Task RefusedFile_WithANewMarker_IsTriedAgain()
    {
        Listing(File("ref-grande", "v1"));
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 1048577));
        await RunAsync();
        _api.Override = null;
        Listing(File("ref-grande", "v2"));

        await RunAsync();

        Assert.Equal(["ref-grande", "ref-grande"], _connector.MarkdownRequests);
        Assert.Equal(2, _api.Upserts(_base.Id).Count);
        Assert.Equal("v2", _api.Documents[_base.Id]["ref-grande"].Version);
    }

    [Theory]
    [InlineData("unsupported-source-type")]
    [InlineData("null-character")]
    [InlineData("empty-content")]
    public async Task OtherDeterministicRefusals_AreRemembered(string code)
    {
        Listing(File("ref-a"));
        RefuseUpsertOf("ref-a", () => FakeSyncApiHandler.ContentRefusal(code));
        await RunAsync();

        await RunAsync();

        Assert.Single(_connector.MarkdownRequests);
        Assert.Equal([("ref-a", code, null)], Ignored(_api.Results(_base.Id)[1]));
    }

    [Theory]
    [InlineData("provider-unavailable")]
    [InlineData("provider-error")]
    [InlineData("file-not-found")]
    public async Task TransientMarkdownFailures_AreNotRemembered(string code)
    {
        Listing(File("ref-a"));
        _connector.Markdown = _ => throw new ConnectorFailure(code);
        await RunAsync();

        await RunAsync();

        Assert.Equal(["ref-a", "ref-a"], _connector.MarkdownRequests);
        Assert.Equal(0, _memory.Count);
    }

    [Fact]
    public async Task RateLimited_IsNotRemembered()
    {
        Listing(File("ref-a"));
        _connector.Markdown = _ => throw new ConnectorFailure("rate-limited");
        await RunAsync();
        _connector.Markdown = _ => "# a\n";

        await RunAsync();

        Assert.Equal(["ref-a", "ref-a"], _connector.MarkdownRequests);
        Assert.Single(_api.Upserts(_base.Id));
    }

    [Fact]
    public async Task Contention_IsNotRemembered()
    {
        Listing(File("ref-a"));
        RefuseUpsertOf("ref-a", FakeSyncApiHandler.Contention);
        await RunAsync();
        _api.Override = null;

        await RunAsync();

        Assert.Equal(["ref-a", "ref-a"], _connector.MarkdownRequests);
        Assert.Equal(2, _api.Upserts(_base.Id).Count);
    }

    [Fact]
    public async Task ReferenceThatLeftTheFolderAndCameBack_IsTriedAgain()
    {
        Listing(File("ref-grande", "v1"));
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 1048577));
        await RunAsync();
        Listing();
        await RunAsync();
        Listing(File("ref-grande", "v1"));

        await RunAsync();

        Assert.Equal(["ref-grande", "ref-grande"], _connector.MarkdownRequests);
    }

    [Fact]
    public async Task BaseGone_ForgetsItsEntries()
    {
        Listing(File("ref-grande", "v1"));
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 1048577));
        await RunAsync();
        Assert.Equal(1, _memory.CountFor(_base.Id));
        _api.Documents.Remove(_base.Id);

        await RunAsync();

        Assert.Equal(0, _memory.CountFor(_base.Id));
    }

    // A listagem que falhou não diz o que sumiu: nada sai da memória por ela.
    [Fact]
    public async Task FailedListing_DoesNotPruneTheMemory()
    {
        Listing(File("ref-grande", "v1"));
        RefuseUpsertOf("ref-grande", () => FakeSyncApiHandler.ContentRefusal("too-large", 1048577));
        await RunAsync();
        _connector.ListRoot = _ => throw new ConnectorFailure("provider-unavailable");

        await RunAsync();

        Assert.Equal(1, _memory.CountFor(_base.Id));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
