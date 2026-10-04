extern alias ApiAssembly;
extern alias ConnectorsAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiConnectorsRoundTrip.Tests.Support;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ApiProgram = ApiAssembly::Program;
using ApiTokenService = ApiAssembly::Buteco.Api.Auth.ITokenService;
using ConnectorsProgram = ConnectorsAssembly::Program;
using ConnectorsSyncInProgress = ConnectorsAssembly::Buteco.Connectors.Sync.SyncInProgress;
using ConnectorsTokenService = ConnectorsAssembly::Buteco.Connectors.Auth.ITokenService;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// O sentido novo, <c>apps/connectors</c> → <c>apps/api</c> (design.md da change
/// ciclo-de-sincronizacao, D11; convenção 11): o token que o <c>apps/connectors</c>
/// assina, aceito pela tabela real do <c>apps/api</c>; a recusa <c>too-large</c> real da
/// #120; e a regra de "mudou" do <c>apps/api</c> (D3 da #102). O provedor é o conector
/// falso; o ciclo roda pelo "Sincronizar agora", e o agendamento está fora da composição.
/// <c>partial</c> da classe existente, para não subir outro contêiner.
/// </summary>
public partial class FolderValidationRoundTripTests
{
    private const string DocMime = "application/vnd.google-apps.document";

    private static readonly TimeSpan SafetyLimit = TimeSpan.FromSeconds(30);

    private async Task<Guid> CreateSyncedBaseAsync(WebApplicationFactory<ApiProgram> api)
    {
        fixture.Fake.Describe = id => new FolderDescription(id, "Pasta sincronizada", $"https://falso.test/{id}");
        var response = await RoundTripFixture.OperatorClient(api)
            .PostAsJsonAsync("/knowledge-bases", SyncedRequest(FakeConnector.Key, NewFolderId()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// "Sincronizar agora" pelo operador, e espera o ciclo terminar: o lock da base é
    /// tomado antes do <c>202</c> e só sai no fim do ciclo (D9).
    /// </summary>
    private static async Task SyncNowAsync(WebApplicationFactory<ConnectorsProgram> connectors, Guid knowledgeBaseId)
    {
        var client = connectors.CreateClient();
        var (token, _) = connectors.Services.GetRequiredService<ConnectorsTokenService>().Issue("operator", TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/connectors/knowledge-bases/{knowledgeBaseId}/sync", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var inProgress = connectors.Services.GetRequiredService<ConnectorsSyncInProgress>();
        using var limit = new CancellationTokenSource(SafetyLimit);
        while (!inProgress.TryStart(knowledgeBaseId))
        {
            await Task.Yield();
            limit.Token.ThrowIfCancellationRequested();
        }

        inProgress.Finish(knowledgeBaseId);
    }

    private static async Task<JsonElement> GetAsync(WebApplicationFactory<ApiProgram> api, string path) =>
        JsonDocument.Parse(await RoundTripFixture.OperatorClient(api).GetStringAsync(path)).RootElement.Clone();

    private static async Task<List<JsonElement>> DocumentsAsync(WebApplicationFactory<ApiProgram> api, Guid knowledgeBaseId) =>
        (await GetAsync(api, $"/knowledge-bases/{knowledgeBaseId}/documents")).EnumerateArray().ToList();

    private static async Task<List<JsonElement>> EventsAsync(WebApplicationFactory<ApiProgram> api, Guid knowledgeBaseId) =>
        (await GetAsync(api, $"/knowledge-bases/{knowledgeBaseId}/document-events")).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task SyncCycle_CreatesUpdatesKeepsUnchangedAndDeletes_AgainstTheRealApi()
    {
        var (api, connectors) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var knowledgeBaseId = await CreateSyncedBaseAsync(api);
        var content = new Dictionary<string, string>
        {
            ["ref-a"] = "# A\n\nPrimeira versão de A.\n",
            ["ref-b"] = "# B\n\nTexto de B.\n",
        };
        fixture.Fake.Markdown = file => content[file.ExternalRef];

        // 1. Dois arquivos novos: criados, cada um com uma publicação de indexação.
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-a", "A.md", "v1", DocMime), new("ref-b", "B.md", "v1", DocMime)], []);
        await SyncNowAsync(connectors, knowledgeBaseId);
        var documents = await DocumentsAsync(api, knowledgeBaseId);
        Assert.Equal(["A.md", "B.md"], documents.Select(document => document.GetProperty("title").GetString()).Order(StringComparer.Ordinal));
        var ids = documents.Select(document => document.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(2, fixture.Indexing.Published.Count(message => ids.Contains(message.KnowledgeDocumentId)));
        var eventsAfterCreate = (await EventsAsync(api, knowledgeBaseId)).Count;

        // 2. A com texto novo: Updated. B com marcador novo e o mesmo texto (o caso do Doc
        //    renomeado): Unchanged, sem evento e sem indexação.
        content["ref-a"] = "# A\n\nSegunda versão de A.\n";
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-a", "A.md", "v2", DocMime), new("ref-b", "B.md", "v2", DocMime)], []);
        await SyncNowAsync(connectors, knowledgeBaseId);
        var events = await EventsAsync(api, knowledgeBaseId);
        Assert.Equal(eventsAfterCreate + 1, events.Count);
        Assert.Equal("A.md", events[0].GetProperty("documentTitle").GetString());
        Assert.Equal(3, fixture.Indexing.Published.Count(message => ids.Contains(message.KnowledgeDocumentId)));
        var refs = await RoundTripFixture.OperatorClient(api).GetFromJsonAsync<JsonElement>($"/sync/knowledge-bases/{knowledgeBaseId}/documents");
        Assert.All(refs.EnumerateArray(), reference => Assert.Equal("v2", reference.GetProperty("externalVersion").GetString()));

        // 2b. B renomeado no Drive, mesmo texto: é mudança de título (D13; cenário
        //     "Renomear o arquivo é mudança de título", corrigido na verificação manual).
        var publishedBeforeRename = fixture.Indexing.Published.Count(message => ids.Contains(message.KnowledgeDocumentId));
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-a", "A.md", "v2", DocMime), new("ref-b", "B renomeado.md", "v3", DocMime)], []);
        await SyncNowAsync(connectors, knowledgeBaseId);
        var renamed = (await EventsAsync(api, knowledgeBaseId))[0];
        Assert.Equal("Updated", renamed.GetProperty("type").GetString());
        Assert.Equal("B renomeado.md", renamed.GetProperty("documentTitle").GetString());
        Assert.True(renamed.GetProperty("titleChanged").GetBoolean());
        Assert.False(renamed.GetProperty("contentChanged").GetBoolean());
        Assert.Equal(publishedBeforeRename, fixture.Indexing.Published.Count(message => ids.Contains(message.KnowledgeDocumentId)));

        // 3. B sumiu da pasta: excluído; A fica.
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-a", "A.md", "v2", DocMime)], []);
        await SyncNowAsync(connectors, knowledgeBaseId);
        Assert.Equal(["A.md"], (await DocumentsAsync(api, knowledgeBaseId)).Select(document => document.GetProperty("title").GetString()));

        // 4. O desfecho gravado no apps/api real.
        var syncState = (await GetAsync(api, $"/knowledge-bases/{knowledgeBaseId}")).GetProperty("syncState");
        Assert.Equal(JsonValueKind.Null, syncState.GetProperty("lastError").ValueKind);
        Assert.Empty(syncState.GetProperty("ignoredFiles").EnumerateArray());
        Assert.Equal("Pasta sincronizada", (await GetAsync(api, $"/knowledge-bases/{knowledgeBaseId}")).GetProperty("syncSource").GetProperty("folderName").GetString());
    }

    // 9.3: o token que o apps/connectors assina é aceito pela tabela real do apps/api.
    [Fact]
    public async Task SyncCycle_TokenOfTheConnectors_IsAcceptedByTheRealApi()
    {
        var (api, connectors) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var knowledgeBaseId = await CreateSyncedBaseAsync(api);
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-token", "Token.md", "v1", DocMime)], []);
        fixture.Fake.Markdown = _ => "# Token\n";
        var before = fixture.ConnectorsToApi.Count;

        await SyncNowAsync(connectors, knowledgeBaseId);

        var calls = fixture.ConnectorsToApi.Skip(before).ToList();
        Assert.Contains(calls, call => call.Method == "PUT");
        Assert.Contains(calls, call => call.Method == "POST" && call.Path.EndsWith("/sync-results", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, call => call.Status is 401 or 403);

        // O apps/api deixa o operador passar em tudo (D6 da #102): sem isto, um token
        // assinado como operator passaria nas asserções acima. O subject é lido pelo
        // TokenService do PRÓPRIO apps/api, sobre o token que chegou nele.
        var apiTokens = api.Services.GetRequiredService<ApiTokenService>();
        Assert.All(calls, call =>
        {
            var validation = apiTokens.Validate(call.Token!);
            Assert.True(validation.IsValid);
            Assert.Equal("service:connectors", validation.Subject);
        });
    }

    // 9.4: a recusa real too-large chega aos ignorados com o código e o tamanho, o
    // documento anterior fica, e o segundo ciclo com o mesmo marcador não envia de novo
    // (D14).
    [Fact]
    public async Task SyncCycle_TooLargeFromTheRealApi_IsIgnoredKeepsTheDocumentAndIsNotResent()
    {
        var (api, connectors) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var knowledgeBaseId = await CreateSyncedBaseAsync(api);
        var large = new string('a', 1024 * 1024 + 1);
        fixture.Fake.ListRoot = _ => new RootListing([new("ref-grande", "Grande.md", "v1", DocMime)], []);
        fixture.Fake.Markdown = _ => "# Pequeno\n";
        await SyncNowAsync(connectors, knowledgeBaseId);

        fixture.Fake.ListRoot = _ => new RootListing([new("ref-grande", "Grande.md", "v2", DocMime)], []);
        fixture.Fake.Markdown = _ => large;
        await SyncNowAsync(connectors, knowledgeBaseId);
        var upsertsAfterRefusal = fixture.ConnectorsToApi.Count(call => call.Method == "PUT" && call.Path.Contains(knowledgeBaseId.ToString(), StringComparison.Ordinal));
        var markdownAfterRefusal = fixture.Fake.MarkdownRequests.Count(reference => reference == "ref-grande");

        await SyncNowAsync(connectors, knowledgeBaseId);

        var syncState = (await GetAsync(api, $"/knowledge-bases/{knowledgeBaseId}")).GetProperty("syncState");
        var ignored = Assert.Single(syncState.GetProperty("ignoredFiles").EnumerateArray());
        Assert.Equal("ref-grande", ignored.GetProperty("externalRef").GetString());
        Assert.Equal("too-large", ignored.GetProperty("code").GetString());
        Assert.Equal((1024 * 1024 + 1).ToString(), ignored.GetProperty("detail").GetString());
        var document = Assert.Single(await DocumentsAsync(api, knowledgeBaseId));
        Assert.Equal(1, document.GetProperty("contentRevision").GetInt32());
        Assert.Equal(upsertsAfterRefusal, fixture.ConnectorsToApi.Count(call => call.Method == "PUT" && call.Path.Contains(knowledgeBaseId.ToString(), StringComparison.Ordinal)));
        Assert.Equal(markdownAfterRefusal, fixture.Fake.MarkdownRequests.Count(reference => reference == "ref-grande"));
    }
}
