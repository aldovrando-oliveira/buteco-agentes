using System.Net;
using System.Text.Json;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Cada resposta do <c>apps/api</c> vira um desfecho tipado (design.md da change
/// ciclo-de-sincronizacao, D4, D7, D8), lido do TEXTO da resposta. Contra o
/// <see cref="FakeSyncApiHandler"/>, cujas respostas de recusa têm a forma medida na
/// tarefa 1.2.
/// </summary>
public class SyncApiClientTests
{
    private readonly FakeSyncApiHandler _api = new();

    private SyncApiClient Client() => new(new SingleClientFactory(new HttpClient(_api) { BaseAddress = new Uri("http://api.test") }));

    [Fact]
    public async Task ListKnowledgeBases_ReadsTheBases()
    {
        var knowledgeBase = _api.AddBase(isActive: false);

        var response = await Client().ListKnowledgeBasesAsync(CancellationToken.None);

        Assert.Equal(SyncApiStatus.Ok, response.Status);
        Assert.Equal([knowledgeBase], response.Value);
    }

    [Fact]
    public async Task ListDocumentRefs_ReadsRefsAndVersions()
    {
        var knowledgeBase = _api.AddBase();
        _api.AddDocument(knowledgeBase.Id, "ref-1", "v1");

        var response = await Client().ListDocumentRefsAsync(knowledgeBase.Id, CancellationToken.None);

        Assert.Equal(SyncApiStatus.Ok, response.Status);
        var reference = Assert.Single(response.Value!);
        Assert.Equal(("ref-1", "v1"), (reference.ExternalRef, reference.ExternalVersion));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task ListDocumentRefs_NotFoundOrManual_IsBaseGone(HttpStatusCode status)
    {
        _api.Override = (_, _) => new HttpResponseMessage(status);

        var response = await Client().ListDocumentRefsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.BaseGone, response.Status);
    }

    [Fact]
    public async Task Upsert_Ok_ReadsTheOutcomeAndSendsTheBody()
    {
        var knowledgeBase = _api.AddBase();

        var response = await Client().UpsertAsync(
            knowledgeBase.Id, new SyncedDocumentUpsert("ref-1", "v1", "Doc.md", "markdown", "# Doc\n"), CancellationToken.None);

        Assert.Equal(SyncApiStatus.Ok, response.Status);
        Assert.Equal("Created", response.Value);
        var body = JsonDocument.Parse(_api.Upserts(knowledgeBase.Id).Single().Body!).RootElement;
        Assert.Equal("ref-1", body.GetProperty("externalRef").GetString());
        Assert.Equal("v1", body.GetProperty("externalVersion").GetString());
        Assert.Equal("Doc.md", body.GetProperty("title").GetString());
        Assert.Equal("markdown", body.GetProperty("sourceType").GetString());
        Assert.Equal("# Doc\n", body.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Upsert_TooLarge_IsContentRefusedWithTheSizeAsDetail()
    {
        _api.Override = (_, _) => FakeSyncApiHandler.ContentRefusal("too-large", contentBytes: 1048577);

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.ContentRefused, response.Status);
        Assert.Equal("too-large", response.Code);
        Assert.Equal("1048577", response.Detail);
    }

    [Theory]
    [InlineData("empty-content")]
    [InlineData("null-character")]
    [InlineData("unsupported-source-type")]
    public async Task Upsert_OtherContentRefusals_HaveNoDetail(string code)
    {
        _api.Override = (_, _) => FakeSyncApiHandler.ContentRefusal(code);

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.ContentRefused, response.Status);
        Assert.Equal(code, response.Code);
        Assert.Null(response.Detail);
    }

    // Forma (#120): 400 sem code é defeito do conector, nunca propriedade do arquivo.
    [Fact]
    public async Task Upsert_ShapeRefusal_IsContractError()
    {
        _api.Override = (_, _) => FakeSyncApiHandler.ShapeRefusal();

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.ContractError, response.Status);
        Assert.Null(response.Code);
    }

    [Fact]
    public async Task Upsert_CodeOutOfTheCodeShape_IsContractError()
    {
        _api.Override = (_, _) => FakeSyncApiHandler.ContentRefusal("Uma frase, não um código");

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.ContractError, response.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, SyncApiStatus.ContractError)]
    [InlineData(HttpStatusCode.BadGateway, SyncApiStatus.ContractError)]
    [InlineData(HttpStatusCode.Unauthorized, SyncApiStatus.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, SyncApiStatus.Rejected)]
    [InlineData(HttpStatusCode.NotFound, SyncApiStatus.BaseGone)]
    public async Task Upsert_OtherStatuses_AreClassified(HttpStatusCode status, SyncApiStatus expected)
    {
        _api.Override = (_, _) => new HttpResponseMessage(status);

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task Upsert_Contention_IsContention()
    {
        _api.Override = (_, _) => FakeSyncApiHandler.Contention();

        var response = await Client().UpsertAsync(Guid.NewGuid(), Upsert(), CancellationToken.None);

        Assert.Equal(SyncApiStatus.Contention, response.Status);
    }

    [Fact]
    public async Task NetworkFailure_IsUnreachable()
    {
        _api.Override = (_, _) => throw new HttpRequestException("conexão recusada");

        var response = await Client().ListKnowledgeBasesAsync(CancellationToken.None);

        Assert.Equal(SyncApiStatus.Unreachable, response.Status);
    }

    // O HttpClient.Timeout chega como TaskCanceledException sem o token de quem chamou.
    [Fact]
    public async Task Timeout_IsUnreachable()
    {
        _api.Override = (_, _) => throw new TaskCanceledException("tempo esgotado", new TimeoutException());

        var response = await Client().ListKnowledgeBasesAsync(CancellationToken.None);

        Assert.Equal(SyncApiStatus.Unreachable, response.Status);
    }

    // Parada da aplicação não é "apps/api fora do ar": propaga.
    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().ListKnowledgeBasesAsync(cancellation.Token));
    }

    [Fact]
    public async Task Delete_EscapesTheReferenceInTheQuery()
    {
        var knowledgeBase = _api.AddBase();
        _api.AddDocument(knowledgeBase.Id, "a/b&c=d", "v1");

        var response = await Client().DeleteAsync(knowledgeBase.Id, "a/b&c=d", CancellationToken.None);

        Assert.Equal(SyncApiStatus.Ok, response.Status);
        Assert.Empty(_api.Documents[knowledgeBase.Id]);
    }

    [Fact]
    public async Task RecordResult_Succeeded_SendsFolderAndIgnoredFilesAndNullError()
    {
        var knowledgeBase = _api.AddBase();

        var response = await Client().RecordResultAsync(
            knowledgeBase.Id,
            SyncCycleResult.Succeeded("Pasta", "https://falso.test/p", [new SyncIgnoredFile("ref-2", "Grande.md", "too-large", "1048577")]),
            CancellationToken.None);

        Assert.Equal(SyncApiStatus.Ok, response.Status);
        var body = Assert.Single(_api.Results(knowledgeBase.Id));
        Assert.Equal("Succeeded", body.GetProperty("outcome").GetString());
        Assert.Equal("Pasta", body.GetProperty("folderName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("error").ValueKind);
        var ignored = Assert.Single(body.GetProperty("ignoredFiles").EnumerateArray());
        Assert.Equal("too-large", ignored.GetProperty("code").GetString());
        Assert.Equal("1048577", ignored.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RecordResult_Failed_SendsOnlyTheError()
    {
        var knowledgeBase = _api.AddBase();

        await Client().RecordResultAsync(knowledgeBase.Id, SyncCycleResult.Failed("access-denied", "conta@falsa.test"), CancellationToken.None);

        var body = Assert.Single(_api.Results(knowledgeBase.Id));
        Assert.Equal("Failed", body.GetProperty("outcome").GetString());
        Assert.Equal("access-denied", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("conta@falsa.test", body.GetProperty("error").GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("folderName").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("ignoredFiles").ValueKind);
    }

    private static SyncedDocumentUpsert Upsert() => new("ref-1", "v1", "Doc.md", "markdown", "# Doc\n");

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
