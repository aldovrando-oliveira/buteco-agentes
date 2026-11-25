using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Providers.Entities;
using Buteco.Api.Providers.Vendors;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static Buteco.Api.Tests.Support.FakeVendorModelsHttpMessageHandler;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// <c>GET /providers/{provider}/vendor-models</c> (design.md da change
/// modelos-curados-por-provedor, D4 e D5; spec <c>llm-model-curation</c>, "Opções de modelo
/// vindas da listagem do fornecedor", "A chave do provedor nunca sai do processo pela listagem"
/// e "Falha do fornecedor só impede habilitar"). O fornecedor é o falso de
/// <see cref="FakeVendorModelsHttpMessageHandler"/>, com os corpos copiados da documentação de
/// 19/11/2026 e da resposta real gravada do gateway.
///
/// <para>
/// <b>O fio é lido como texto</b> sempre que o nome ou a ausência de um campo é o que se afirma
/// (<c>filter</c>, <c>vendorStatus</c>, nenhum <c>onlyChatModels</c>): desserializar para o mesmo
/// tipo seria cego a isso (convenção 12).
/// </para>
/// </summary>
public class VendorModelsEndpointTests(VendorModelsEndpointTests.Fixture factory) : IClassFixture<VendorModelsEndpointTests.Fixture>, IAsyncLifetime
{
    public sealed class Fixture : ModelCurationFixture;

    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        factory.ResetKeys();
        factory.Logs.Clear();

        // O host é um só para a classe: sem zerar, os contadores dos fornecedores falsos somam
        // as chamadas dos casos anteriores, e "nenhuma chamada" fica falso por arranjo.
        foreach (var provider in new[] { "openai", "anthropic", "gemini" })
        {
            factory.Vendor(provider).Reset(Always(HttpStatusCode.OK, "{}"));
        }

        _admin = await AdminClientAsync(factory);
    }

    public Task DisposeAsync()
    {
        factory.ResetKeys();
        return Task.CompletedTask;
    }

    public static async Task<HttpClient> AdminClientAsync(ModelCurationFixture factory, string prefix = "admin-curadoria")
    {
        var admin = await SignedInUser.CreateAsync(factory, prefix);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        return client;
    }

    // ------------------------------------------------------------- O CONTRATO E O FILTRO

    [Fact]
    public async Task OpenAi_ListsEverything_AndTheWireSaysFilterNone()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.OK, """
            {"object":"list","data":[
              {"id":"text-embedding-3-large","object":"model","created":1,"owned_by":"system"},
              {"id":"gpt-5.6-sol","object":"model","created":1,"owned_by":"system"}]}
            """));

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"filter\":\"none\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("onlyChatModels", json, StringComparison.Ordinal);
        var ids = Ids(json);
        Assert.Equal(["gpt-5.6-sol", "text-embedding-3-large"], ids);
        Assert.Equal("https://gateway-compativel.test/v1/models", Assert.Single(factory.OpenAi.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Gemini_ListsOnlyGenerateContent_WithoutThePrefix()
    {
        factory.SetKeys(gemini: true);
        factory.Gemini.Reset(Always(HttpStatusCode.OK, """
            {"models":[
              {"name":"models/gemini-x","displayName":"Gemini X","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/embedding-y","displayName":"Embedding Y","supportedGenerationMethods":["embedContent"]}]}
            """));

        var (status, json) = await GetTextAsync("/providers/gemini/vendor-models");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"filter\":\"supports-generate-content\"", json, StringComparison.Ordinal);
        Assert.Equal(["gemini-x"], Ids(json));
        Assert.Contains("\"displayName\":\"Gemini X\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anthropic_WalksThePages_WithAfterIdEqualToTheLastId()
    {
        factory.SetKeys(anthropic: true);
        factory.Anthropic.Reset((request, _) => Task.FromResult(request.RequestUri!.Query.Contains("after_id=", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, """{"data":[{"type":"model","id":"claude-b","display_name":"Claude B","lifecycle":"active"}],"has_more":false,"first_id":"claude-b","last_id":"claude-b"}""")
            : Json(HttpStatusCode.OK, """{"data":[{"type":"model","id":"claude-a","display_name":"Claude A","lifecycle":"active"}],"has_more":true,"first_id":"claude-a","last_id":"claude-a"}""")));

        var (status, json) = await GetTextAsync("/providers/anthropic/vendor-models");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"filter\":\"lifecycle-active\"", json, StringComparison.Ordinal);
        Assert.Equal(["claude-a", "claude-b"], Ids(json));
        var requests = factory.Anthropic.Requests;
        Assert.Equal(2, requests.Count);
        Assert.Contains("lifecycle=active", requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("after_id=claude-a", requests[1].Uri.Query, StringComparison.Ordinal);
    }

    /// <summary>R12: o servidor devolveu um <c>deprecated</c> apesar do parâmetro, e a rota não o oferece.</summary>
    [Fact]
    public async Task Anthropic_DoesNotOfferADeprecatedItemTheServerReturned()
    {
        factory.SetKeys(anthropic: true);
        factory.Anthropic.Reset(Always(HttpStatusCode.OK, """
            {"data":[
              {"type":"model","id":"claude-ativo","display_name":"Ativo","lifecycle":"active"},
              {"type":"model","id":"claude-velho","display_name":"Velho","lifecycle":"deprecated"}],
             "has_more":false,"first_id":"claude-ativo","last_id":"claude-velho"}
            """));

        var (status, json) = await GetTextAsync("/providers/anthropic/vendor-models");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["claude-ativo"], Ids(json));
    }

    [Fact]
    public async Task Anthropic_ItemWithoutLifecycle_IsOutOfContract()
    {
        factory.SetKeys(anthropic: true);
        factory.Anthropic.Reset(Always(HttpStatusCode.OK, """{"data":[{"type":"model","id":"claude-x"}],"has_more":false,"first_id":"claude-x","last_id":"claude-x"}"""));

        var (status, json) = await GetTextAsync("/providers/anthropic/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("\"code\":\"vendor-error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vendorStatus\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEnabledModel_ComesMarked()
    {
        await EnableDirectlyAsync("openai", "modelo-marcado");
        factory.OpenAi.Reset(Always(HttpStatusCode.OK, """{"object":"list","data":[{"id":"modelo-marcado"},{"id":"modelo-livre"}]}"""));

        var (_, json) = await GetTextAsync("/providers/openai/vendor-models");

        using var document = JsonDocument.Parse(json);
        var models = document.RootElement.GetProperty("models").EnumerateArray().ToDictionary(
            model => model.GetProperty("id").GetString()!, model => model.GetProperty("enabled").GetBoolean());
        Assert.True(models["modelo-marcado"]);
        Assert.False(models["modelo-livre"]);
    }

    /// <summary>A resposta real gravada do gateway compatível em 19/11/2026.</summary>
    [Fact]
    public async Task AVendorThatListsNothing_Is200_WithAnEmptyList()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.OK, """{"object":"list","data":[]}"""));

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("\"models\":[]", json, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- A CHAVE (R2)

    [Fact]
    public async Task TheGeminiKey_GoesInTheHeader_AndNeverInTheUrl()
    {
        factory.SetKeys(gemini: true);
        factory.Gemini.Reset(Always(HttpStatusCode.OK, """{"models":[]}"""));

        await GetTextAsync("/providers/gemini/vendor-models");

        var request = Assert.Single(factory.Gemini.Requests);
        Assert.Equal(ModelCurationFixture.GeminiKey, request.Headers["x-goog-api-key"]);
        Assert.DoesNotContain("key=", request.Uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(ModelCurationFixture.GeminiKey, request.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAnthropicHeaders_AreTheOnesTheWorkersSdkSends()
    {
        factory.SetKeys(anthropic: true);
        factory.Anthropic.Reset(Always(HttpStatusCode.OK, """{"data":[],"has_more":false,"first_id":null,"last_id":null}"""));

        await GetTextAsync("/providers/anthropic/vendor-models");

        var request = Assert.Single(factory.Anthropic.Requests);
        Assert.Equal(ModelCurationFixture.AnthropicKey, request.Headers["x-api-key"]);
        Assert.Equal(AnthropicModelLister.ApiVersion, request.Headers["anthropic-version"]);
        Assert.Equal("2023-06-01", AnthropicModelLister.ApiVersion);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    public async Task On401_NeitherTheLogNorTheResponseCarryTheKey_OrTheVendorBody(string provider)
    {
        factory.SetKeys(anthropic: true, gemini: true);
        const string vendorBody = """{"error":{"message":"chave inválida: eco-do-fornecedor"}}""";
        factory.Vendor(provider).Reset(Always(HttpStatusCode.Unauthorized, vendorBody));
        factory.Logs.Clear();

        var (status, json) = await GetTextAsync($"/providers/{provider}/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        foreach (var key in new[] { ModelCurationFixture.OpenAiKey, ModelCurationFixture.AnthropicKey, ModelCurationFixture.GeminiKey })
        {
            Assert.DoesNotContain(key, json, StringComparison.Ordinal);
            Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(key, StringComparison.Ordinal));
        }

        Assert.DoesNotContain("eco-do-fornecedor", json, StringComparison.Ordinal);
        Assert.Contains(factory.Logs.Lines, line => line.Contains("Listagem de modelos do fornecedor recusada", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- AS FALHAS (R5, R14)

    [Fact]
    public async Task AnUnknownProvider_Is404_WithoutCallingAnyVendor()
    {
        var (status, json) = await GetTextAsync("/providers/mistral/vendor-models");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("\"code\":\"provider-not-found\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("vendorStatus", json, StringComparison.Ordinal);
        Assert.Equal(0, factory.VendorRequestCount);
    }

    [Fact]
    public async Task AProviderWithoutKey_Is409_WithoutCallingTheVendor()
    {
        factory.Anthropic.Reset(Always(HttpStatusCode.OK, "{}"));

        var (status, json) = await GetTextAsync("/providers/anthropic/vendor-models");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("\"code\":\"provider-not-configured\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("vendorStatus", json, StringComparison.Ordinal);
        Assert.Equal(0, factory.Anthropic.RequestCount);
    }

    [Fact]
    public async Task AGatewayWithoutTheListRoute_Is502_WithVendorStatus404()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.NotFound, "não há /models aqui"));

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("\"code\":\"vendor-error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vendorStatus\":404", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeyRefusedByTheVendor_Is502_WithVendorStatus401()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.Unauthorized, "{}"));

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("\"code\":\"vendor-error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vendorStatus\":401", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A200OutOfContract_Is502_WithVendorStatusNullWritten()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.OK, """{"object":"list"}"""));

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("\"code\":\"vendor-error\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vendorStatus\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreThanTenPages_Is502_WithVendorStatusNull()
    {
        factory.SetKeys(gemini: true);
        factory.Gemini.Reset(Always(HttpStatusCode.OK, """{"models":[{"name":"models/sem-fim","supportedGenerationMethods":["generateContent"]}],"nextPageToken":"sempre-mais"}"""));

        var (status, json) = await GetTextAsync("/providers/gemini/vendor-models");

        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("\"vendorStatus\":null", json, StringComparison.Ordinal);
        Assert.Equal(VendorModelCatalog.MaxPages, factory.Gemini.RequestCount);
    }

    [Fact]
    public async Task AVendorThatNeverAnswers_Is503_WithinTheBudget_AndWithoutVendorStatus()
    {
        factory.OpenAi.Reset(Never());
        var started = DateTimeOffset.UtcNow;

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("\"code\":\"vendor-unavailable\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("vendorStatus", json, StringComparison.Ordinal);
        Assert.True(DateTimeOffset.UtcNow - started < VendorModelCatalog.Budget + TimeSpan.FromSeconds(10), "a resposta passou do orçamento");
    }

    [Fact]
    public async Task AnUnreachableVendor_Is503()
    {
        factory.OpenAi.Reset(Unreachable());

        var (status, json) = await GetTextAsync("/providers/openai/vendor-models");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("\"code\":\"vendor-unavailable\"", json, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- APOIO

    private async Task<(HttpStatusCode Status, string Json)> GetTextAsync(string path)
    {
        using var response = await _admin.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static List<string> Ids(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("id").GetString()!).ToList();
    }

    private async Task EnableDirectlyAsync(string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!dbContext.EnabledLlmModels.Any(enabled => enabled.Provider == provider && enabled.Model == model))
        {
            dbContext.EnabledLlmModels.Add(new EnabledLlmModel(provider, model));
            await dbContext.SaveChangesAsync();
        }
    }
}
