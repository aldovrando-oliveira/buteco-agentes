using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Buteco.Api.Auth;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeSync.Connectors;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Cadastro de base <c>Synced</c> pela rota do operador, com a pasta validada pelo
/// <c>apps/connectors</c> (design.md da change criacao-base-sincronizada).
/// <c>partial</c> da classe existente: nenhuma fonte de contêiner nova (D9).
///
/// <para>
/// A fixture sobe SEM <c>Connectors:BaseUrl</c>. Os casos que precisam do
/// <c>apps/connectors</c> derivam um host da fixture com
/// <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>: mesmo Postgres,
/// mais o endereço e o <see cref="FakeConnectorsHttpMessageHandler"/> como handler
/// primário do <c>HttpClient</c> nomeado. O token <c>service:api</c> continua sendo
/// assinado pelo handler real, que fica por cima do falso.
/// </para>
/// </summary>
public partial class KnowledgeBaseCatalogTests
{
    private sealed record ConnectorsHost(
        WebApplicationFactory<Program> Factory,
        HttpClient Client,
        FakeConnectorsHttpMessageHandler Connectors,
        LogEventCounter RaceCounter) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private ConnectorsHost WithConnectors(TimeSpan? clientTimeout = null)
    {
        var connectors = new FakeConnectorsHttpMessageHandler();
        var raceCounter = new LogEventCounter(CreateKnowledgeBaseCommandHandler.ConcurrentSyncedCreateRejectedEvent.Id);

        var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Connectors:BaseUrl"] = "http://connectors.test",
                }));
            builder.ConfigureLogging(logging => logging.AddProvider(raceCounter));
            builder.ConfigureServices(services =>
            {
                var http = services.AddHttpClient(ConnectorsFolderClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => connectors);
                if (clientTimeout is not null)
                {
                    http.ConfigureHttpClient(client => client.Timeout = clientTimeout.Value);
                }
            });
        });

        var client = derived.CreateClient();
        TestAuthentication.AttachOperatorToken(client, derived.Services);
        return new ConnectorsHost(derived, client, connectors, raceCounter);
    }

    private static string NewFolderId(string prefix = "pasta") => $"{prefix}-{Guid.NewGuid():N}";

    private static object SyncedRequest(string name, string folderId, string provider = "google-drive") => new
    {
        name,
        description = "Base acompanhando uma pasta.",
        contentMode = "Synced",
        provider,
        folderId,
    };

    private async Task<List<KnowledgeBase>> BasesWithFolderAsync(string folderId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeBases.AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.SyncFolderId == folderId)
            .ToListAsync();
    }

    private async Task<int> BasesNamedAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeBases.CountAsync(knowledgeBase => knowledgeBase.Name == name);
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // --- 4.7: criação, snapshots e forma ----------------------------------------

    [Fact]
    public async Task CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody()
    {
        await using var host = WithConnectors();
        var folderId = NewFolderId();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", new
        {
            name = "Base do Drive",
            description = "Base acompanhando uma pasta.",
            contentMode = "Synced",
            provider = "google-drive",
            folderId,
            folderName = "Nome forjado pelo corpo",
            folderUrl = "https://forjado.test/pasta",
        });
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Synced", body.GetProperty("contentMode").GetString());
        var source = body.GetProperty("syncSource");
        Assert.Equal("google-drive", source.GetProperty("provider").GetString());
        Assert.Equal(folderId, source.GetProperty("folderId").GetString());
        Assert.Equal(FakeConnectorsHttpMessageHandler.FolderName, source.GetProperty("folderName").GetString());
        Assert.Equal(FakeConnectorsHttpMessageHandler.FolderUrl(folderId), source.GetProperty("folderUrl").GetString());
        Assert.DoesNotContain("forjado", text, StringComparison.OrdinalIgnoreCase);

        var stored = Assert.Single(await BasesWithFolderAsync(folderId));
        Assert.Equal(KnowledgeBaseContentMode.Synced, stored.ContentMode);
        Assert.Equal("google-drive", stored.SyncProvider);
        Assert.Equal(FakeConnectorsHttpMessageHandler.FolderName, stored.SyncFolderName);
        Assert.Equal(FakeConnectorsHttpMessageHandler.FolderUrl(folderId), stored.SyncFolderUrl);
        Assert.Equal(1, host.Connectors.RequestCount);
    }

    [Fact]
    public async Task CreateSynced_StartsWithoutAnySyncState()
    {
        await using var host = WithConnectors();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base nova sincronizada", NewFolderId()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var state = (await BodyAsync(response)).GetProperty("syncState");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastCompletedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastFinishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("failingSince").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastError").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("ignoredFiles").ValueKind);
    }

    [Fact]
    public async Task CreateSynced_StoresTheFolderIdAsSent()
    {
        await using var host = WithConnectors();
        var folderId = $"AbC-{Guid.NewGuid():N}";

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base com caixa", folderId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(folderId, Assert.Single(await BasesWithFolderAsync(folderId)).SyncFolderId);
    }

    [Theory]
    [InlineData("provider", "")]
    [InlineData("provider", "   ")]
    [InlineData("provider", "google-drive")]
    [InlineData("folderId", "")]
    [InlineData("folderId", "   ")]
    [InlineData("folderId", "pasta-1")]
    public async Task CreateManual_WithProviderOrFolderValue_IsRefusedWithoutCallingConnectors(string field, string value)
    {
        await using var host = WithConnectors();
        var name = $"Base manual com {field} {Guid.NewGuid():N}";

        foreach (var contentMode in new[] { null, "Manual" })
        {
            var body = new JsonObject { ["name"] = name, ["description"] = "Descrição.", [field] = value };
            if (contentMode is not null)
            {
                body["contentMode"] = contentMode;
            }

            var response = await host.Client.PostAsJsonAsync("/knowledge-bases", body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
        }

        Assert.Equal(0, await BasesNamedAsync(name));
        Assert.Equal(0, host.Connectors.RequestCount);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("folderId")]
    public async Task CreateManual_WithNullProviderOrFolder_IsAccepted(string field)
    {
        await using var host = WithConnectors();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", new JsonObject
        {
            ["name"] = $"Base manual com {field} nulo",
            ["description"] = "Descrição.",
            ["contentMode"] = "Manual",
            [field] = null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("Manual", body.GetProperty("contentMode").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("syncSource").ValueKind);
        Assert.Equal(0, host.Connectors.RequestCount);
    }

    public static TheoryData<string, string?, string?> InvalidSyncedShapes() => new()
    {
        { "provider", null, "pasta-1" },
        { "folderId", "google-drive", null },
        { "provider", "../x", "pasta-1" },
        { "provider", "Google Drive", "pasta-1" },
        { "provider", "google-drive\n", "pasta-1" },
        { "provider", new string('a', 65), "pasta-1" },
        { "folderId", "google-drive", "   " },
        { "folderId", "google-drive", "" },
        { "folderId", "google-drive", new string('p', 257) },
    };

    [Theory]
    [MemberData(nameof(InvalidSyncedShapes))]
    public async Task CreateSynced_WithInvalidShape_IsRefusedBeforeAnyConnectorsCall(string field, string? provider, string? folderId)
    {
        await using var host = WithConnectors();
        var name = $"Base sincronizada malformada {Guid.NewGuid():N}";

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", new
        {
            name,
            description = "Descrição.",
            contentMode = "Synced",
            provider,
            folderId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, await BasesNamedAsync(name));
        Assert.Equal(0, host.Connectors.RequestCount);
    }

    [Fact]
    public async Task CreateSynced_FolderIdAtTheLimit_IsAccepted()
    {
        await using var host = WithConnectors();
        var folderId = $"{Guid.NewGuid():N}{new string('p', 256 - 32)}";

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base com id no limite", folderId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // --- 4.8: pasta em uso --------------------------------------------------------

    private static readonly string[] DeletionWords = ["exclu", "remov", "apag"];

    private static void AssertNoDeletionAdvice(string text)
    {
        foreach (var word in DeletionWords)
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt(bool existingIsActive)
    {
        await using var host = WithConnectors();
        var folderId = NewFolderId();
        var existing = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(
            factory.Services, name: $"Atendimento {Guid.NewGuid():N}", folderId: folderId, isActive: existingIsActive);

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Segunda base da pasta", folderId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("folder-in-use", body.GetProperty("code").GetString());
        Assert.Equal(existing.Id, body.GetProperty("knowledgeBaseId").GetGuid());
        Assert.Equal(existing.Name, body.GetProperty("knowledgeBaseName").GetString());
        var detail = body.GetProperty("detail").GetString()!;
        Assert.Contains(existing.Name, detail, StringComparison.Ordinal);
        Assert.Contains("inativa", detail, StringComparison.Ordinal);
        AssertNoDeletionAdvice(text);

        Assert.Single(await BasesWithFolderAsync(folderId));
        Assert.Equal(0, host.Connectors.RequestCount);
    }

    [Fact]
    public async Task CreateSynced_FolderAlreadyUsed_Returns409EvenWithoutConnectorsConfigured()
    {
        var folderId = NewFolderId();
        var existing = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, name: "Base dona da pasta", folderId: folderId);

        // _client é o da fixture, sem Connectors:BaseUrl.
        var response = await _client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Outra base", folderId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(existing.Name, (await BodyAsync(response)).GetProperty("knowledgeBaseName").GetString());
    }

    [Fact]
    public async Task CreateSynced_FolderAlreadyUsed_Returns409EvenWithConnectorsDown()
    {
        await using var host = WithConnectors();
        host.Connectors.Respond = (_, _) => throw new HttpRequestException("Connection refused");
        var folderId = NewFolderId();
        await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, folderId: folderId);

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Outra base", folderId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, host.Connectors.RequestCount);
    }

    [Fact]
    public async Task CreateSynced_FolderIdsDifferingOnlyInCase_DoNotConflict()
    {
        await using var host = WithConnectors();
        var suffix = Guid.NewGuid().ToString("N");
        await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, folderId: $"AbC-{suffix}");

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base abc", $"abc-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // --- 4.9: corrida -----------------------------------------------------------

    /// <summary>
    /// A barreira do <c>apps/connectors</c> falso segura as duas descrições até as duas
    /// chegarem: as duas requisições passaram pela consulta de pasta em uso, com o banco
    /// ainda sem a pasta, antes de qualquer gravação. Uma delas PRECISA cair no índice.
    /// O evento 1024 é a prova de que o caminho de <c>UniqueViolation</c> rodou.
    /// </summary>
    [Fact]
    public async Task CreateSynced_TwoConcurrentCreatesOfTheSameFolder_OneCreatesTheOtherGets409()
    {
        const int iterations = 20;
        await using var host = WithConnectors();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var folderId = NewFolderId("corrida");
            var before = host.RaceCounter.Count;
            host.Connectors.HoldUntil(2);

            var responses = await Task.WhenAll(
                host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest($"Corrida A {iteration}", folderId)),
                host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest($"Corrida B {iteration}", folderId)));

            Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
            var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

            var winnerName = (await BodyAsync(created)).GetProperty("name").GetString();
            var conflictBody = await conflict.Content.ReadAsStringAsync();
            Assert.Equal(winnerName, JsonDocument.Parse(conflictBody).RootElement.GetProperty("knowledgeBaseName").GetString());
            AssertNoDeletionAdvice(conflictBody);
            Assert.Single(await BasesWithFolderAsync(folderId));
            Assert.Equal(before + 1, host.RaceCounter.Count);
        }

        Assert.Equal(iterations, host.RaceCounter.Count);
    }

    // --- 4.10: falhas do apps/connectors pela rota --------------------------------

    public static TheoryData<string, string?, HttpStatusCode, HttpStatusCode> ConnectorFailures() => new()
    {
        { "access-denied", "leitor@projeto.iam.gserviceaccount.com", HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity },
        { "not-a-folder", null, HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity },
        { "folder-trashed", null, HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity },
        { "provider-not-configured", null, HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity },
        { "api-not-configured", null, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway },
        { "rate-limited", null, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable },
        { "provider-unavailable", null, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(ConnectorFailures))]
    public async Task CreateSynced_ConnectorsFailure_ReachesTheClientAsCodeAndCreatesNothing(
        string code, string? detail, HttpStatusCode connectorsStatus, HttpStatusCode expectedStatus)
    {
        await using var host = WithConnectors();
        host.Connectors.Respond = (_, _) => Task.FromResult(FakeConnectorsHttpMessageHandler.Problem(connectorsStatus, code, detail));
        var folderId = NewFolderId();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base recusada", folderId));

        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        if (detail is null)
        {
            Assert.True(!body.TryGetProperty("detail", out var value) || value.ValueKind == JsonValueKind.Null);
        }
        else
        {
            Assert.Equal(detail, body.GetProperty("detail").GetString());
        }

        Assert.Empty(await BasesWithFolderAsync(folderId));
    }

    [Fact]
    public async Task CreateSynced_WithoutConnectorsConfigured_Is503AndOtherRoutesKeepWorking()
    {
        var folderId = NewFolderId();

        // _client é o da fixture, sem Connectors:BaseUrl.
        var synced = await _client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base sem conectores", folderId));
        var manual = await _client.PostAsJsonAsync("/knowledge-bases", new { name = "Base manual sem conectores", description = "Descrição." });
        var list = await _client.GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, synced.StatusCode);
        Assert.Equal(ConnectorsFailureCodes.NotConfigured, (await BodyAsync(synced)).GetProperty("code").GetString());
        Assert.Empty(await BasesWithFolderAsync(folderId));
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task CreateSynced_ConnectorsDown_Is503Unavailable()
    {
        await using var host = WithConnectors();
        host.Connectors.Respond = (_, _) => throw new HttpRequestException("Connection refused");
        var folderId = NewFolderId();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base com conectores fora", folderId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ConnectorsFailureCodes.Unavailable, (await BodyAsync(response)).GetProperty("code").GetString());
        Assert.Empty(await BasesWithFolderAsync(folderId));
    }

    [Fact]
    public async Task CreateSynced_ConnectorsSlow_Is503UnavailableAndNever500()
    {
        await using var host = WithConnectors(clientTimeout: TimeSpan.FromMilliseconds(300));
        host.Connectors.Respond = async (request, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return FakeConnectorsHttpMessageHandler.DescribeRequestedFolder(request);
        };
        var folderId = NewFolderId();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base com conectores lentos", folderId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(ConnectorsFailureCodes.Unavailable, (await BodyAsync(response)).GetProperty("code").GetString());
        Assert.Empty(await BasesWithFolderAsync(folderId));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task CreateSynced_ConnectorsRejectsTheToken_Is502AndNever401Nor403(HttpStatusCode connectorsStatus)
    {
        await using var host = WithConnectors();
        host.Connectors.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(connectorsStatus));
        var folderId = NewFolderId();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base com token recusado", folderId));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(ConnectorsFailureCodes.Error, body.GetProperty("code").GetString());
        Assert.Equal(((int)connectorsStatus).ToString(), body.GetProperty("detail").GetString());
        Assert.Empty(await BasesWithFolderAsync(folderId));
    }

    // --- 4.11: token e limite da composição real ---------------------------------

    [Fact]
    public async Task CreateSynced_SendsServiceApiTokenToConnectors()
    {
        await using var host = WithConnectors();

        var response = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base do token", NewFolderId()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (uri, authorization) = Assert.Single(host.Connectors.Requests);
        Assert.Equal("/connectors/providers/google-drive/folder", uri.AbsolutePath);
        Assert.NotNull(authorization);
        Assert.Equal("Bearer", authorization!.Scheme);
        var validation = host.Factory.Services.GetRequiredService<ITokenService>().Validate(authorization.Parameter!);
        Assert.True(validation.IsValid);
        Assert.Equal("service:api", validation.Subject);
    }

    [Fact]
    public void RealComposition_ConnectorsClientHasThe35SecondLimit()
    {
        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ConnectorsFolderClient.HttpClientName);

        Assert.Equal(TimeSpan.FromSeconds(35), client.Timeout);
    }
}
