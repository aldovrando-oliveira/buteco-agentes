using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Providers.Entities;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Buteco.Api.Tests.Support.FakeVendorModelsHttpMessageHandler;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// As rotas de curadoria (design.md da change modelos-curados-por-provedor, D6; spec
/// <c>llm-model-curation</c>, "Leitura da lista curada pelo Admin", "Habilitar só o que o
/// fornecedor listou", "Retirar nunca depende do fornecedor" e "Curadoria só do Admin, com o
/// papel gravado"; <c>route-roles</c>).
///
/// <para>
/// Cada caso usa ids de modelo próprios: o host é um só para a classe, e a lista curada fica
/// entre um caso e o outro.
/// </para>
/// </summary>
public class ModelCurationEndpointsTests(ModelCurationEndpointsTests.Fixture factory) : IClassFixture<ModelCurationEndpointsTests.Fixture>, IAsyncLifetime
{
    public sealed class Fixture : ModelCurationFixture;

    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        factory.ResetKeys();
        foreach (var provider in new[] { "openai", "anthropic", "gemini" })
        {
            factory.Vendor(provider).Reset(Always(HttpStatusCode.OK, "{}"));
        }

        _admin = await VendorModelsEndpointTests.AdminClientAsync(factory);
    }

    public Task DisposeAsync()
    {
        factory.ResetKeys();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- LER

    [Fact]
    public async Task TheCuratedList_HasTheThreeProviders_AndTheOneWithoutKeyIsNotConfigured()
    {
        await EnableDirectlyAsync("gemini", "gemini-da-lista");

        var json = await GetTextAsync("/providers/models");

        using var document = JsonDocument.Parse(json);
        var providers = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(["openai", "anthropic", "gemini"], providers.Select(p => p.GetProperty("provider").GetString()));
        var gemini = providers[2];
        Assert.False(gemini.GetProperty("configured").GetBoolean());
        Assert.Contains("gemini-da-lista", gemini.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("id").GetString()));
        Assert.True(providers[0].GetProperty("configured").GetBoolean());
        Assert.Equal(0, factory.VendorRequestCount);
    }

    [Fact]
    public async Task TheAgentCount_IsMeasured_CountingActiveAndInactive_AndZeroWhenNone()
    {
        await EnableDirectlyAsync("openai", "modelo-contado");
        await EnableDirectlyAsync("openai", "modelo-sem-agente");
        await InsertAgentAsync("openai", "modelo-contado", isActive: true);
        await InsertAgentAsync("openai", "modelo-contado", isActive: false);

        var json = await GetTextAsync("/providers/models");

        Assert.Contains("{\"id\":\"modelo-contado\",\"agentCount\":2}", json, StringComparison.Ordinal);
        Assert.Contains("{\"id\":\"modelo-sem-agente\",\"agentCount\":0}", json, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- HABILITAR

    [Fact]
    public async Task EnablingAListedModel_Is201_AndItAppearsInProviders()
    {
        factory.SetKeys(gemini: true);
        factory.Gemini.Reset(Always(HttpStatusCode.OK, """{"models":[{"name":"models/gemini-nova","supportedGenerationMethods":["generateContent"]}]}"""));

        using var response = await _admin.PostAsJsonAsync("/providers/gemini/models", new { model = "gemini-nova" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var providers = await _admin.GetStringAsync("/providers");
        Assert.Contains("gemini-nova", providers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIdTheVendorDoesNotList_Is422_AndNothingIsWritten()
    {
        factory.OpenAi.Reset(Always(HttpStatusCode.OK, """{"object":"list","data":[{"id":"outro-modelo"}]}"""));

        using var response = await _admin.PostAsJsonAsync("/providers/openai/models", new { model = "digitado-livre" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"code\":\"model-not-listed\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await IsEnabledAsync("openai", "digitado-livre"));
    }

    [Fact]
    public async Task AGeminiEmbeddingModel_DoesNotEnable()
    {
        factory.SetKeys(gemini: true);
        factory.Gemini.Reset(Always(HttpStatusCode.OK, """{"models":[{"name":"models/embedding-z","supportedGenerationMethods":["embedContent"]}]}"""));

        using var response = await _admin.PostAsJsonAsync("/providers/gemini/models", new { model = "embedding-z" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(await IsEnabledAsync("gemini", "embedding-z"));
    }

    [Fact]
    public async Task AnAnthropicDeprecatedModel_DoesNotEnable()
    {
        factory.SetKeys(anthropic: true);
        factory.Anthropic.Reset(Always(HttpStatusCode.OK, """{"data":[{"type":"model","id":"claude-descontinuado","lifecycle":"deprecated"}],"has_more":false,"first_id":null,"last_id":null}"""));

        using var response = await _admin.PostAsJsonAsync("/providers/anthropic/models", new { model = "claude-descontinuado" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"code\":\"model-not-listed\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVendorOutOfService_PreventsEnabling_AndNothingIsWritten()
    {
        factory.OpenAi.Reset(Unreachable());

        using var response = await _admin.PostAsJsonAsync("/providers/openai/models", new { model = "modelo-sem-fornecedor" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(await IsEnabledAsync("openai", "modelo-sem-fornecedor"));
    }

    [Fact]
    public async Task EnablingAgain_Is200_WithoutCallingTheVendor()
    {
        await EnableDirectlyAsync("openai", "modelo-repetido");
        factory.OpenAi.Reset(Unreachable());

        using var response = await _admin.PostAsJsonAsync("/providers/openai/models", new { model = "modelo-repetido" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.OpenAi.RequestCount);
    }

    [Fact]
    public async Task AnAlreadyEnabledPair_WithTheProviderWithoutKey_Is200_WithoutCallingTheVendor()
    {
        await EnableDirectlyAsync("gemini", "gemini-ja-habilitado");

        using var response = await _admin.PostAsJsonAsync("/providers/gemini/models", new { model = "gemini-ja-habilitado" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.Gemini.RequestCount);
    }

    [Fact]
    public async Task ANewPair_WithTheProviderWithoutKey_Is409_WithoutCallingTheVendor()
    {
        using var response = await _admin.PostAsJsonAsync("/providers/gemini/models", new { model = "gemini-sem-chave" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("\"code\":\"provider-not-configured\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, factory.Gemini.RequestCount);
    }

    [Fact]
    public async Task EnablingWithoutModel_Is400_WithoutCallingTheVendor()
    {
        using var missing = await _admin.PostAsJsonAsync("/providers/openai/models", new { });
        using var empty = await _admin.PostAsJsonAsync("/providers/openai/models", new { model = "" });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("\"model\"", await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, factory.OpenAi.RequestCount);
    }

    /// <summary>
    /// R13: o outro pedido grava o par durante a consulta ao fornecedor, depois de este passar
    /// pela conferência de par já habilitado. Determinístico: o lister falso grava por outro
    /// contexto, e o <c>SaveChanges</c> da rota encontra a violação da chave primária.
    /// </summary>
    [Fact]
    public async Task TwoRequestsForTheSamePair_AnswerTheSecondWith200_NeverWith500()
    {
        factory.OpenAi.Reset(async (_, _) =>
        {
            await EnableDirectlyAsync("openai", "modelo-corrida");
            return Json(HttpStatusCode.OK, """{"object":"list","data":[{"id":"modelo-corrida"}]}""");
        });

        using var response = await _admin.PostAsJsonAsync("/providers/openai/models", new { model = "modelo-corrida" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountPairAsync("openai", "modelo-corrida"));
    }

    // ------------------------------------------------------------- RETIRAR (R7)

    [Fact]
    public async Task Retiring_WithTheVendorOutAndTheProviderWithoutKey_Is204_WithoutCallingTheVendor()
    {
        await EnableDirectlyAsync("gemini", "gemini-a-retirar");
        factory.Gemini.Reset(Unreachable());

        using var response = await _admin.DeleteAsync("/providers/gemini/models?model=gemini-a-retirar");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await IsEnabledAsync("gemini", "gemini-a-retirar"));
        Assert.Equal(0, factory.VendorRequestCount);
    }

    [Fact]
    public async Task Retiring_AnIdWithASlash_Works()
    {
        await EnableDirectlyAsync("openai", "org/modelo-x");

        using var response = await _admin.DeleteAsync($"/providers/openai/models?model={Uri.EscapeDataString("org/modelo-x")}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await IsEnabledAsync("openai", "org/modelo-x"));
    }

    [Fact]
    public async Task RetiringWithoutModel_Is400()
    {
        using var missing = await _admin.DeleteAsync("/providers/openai/models");
        using var empty = await _admin.DeleteAsync("/providers/openai/models?model=");

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("\"model\"", await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetiringWhatIsNotEnabled_Is404ModelNotEnabled()
    {
        using var response = await _admin.DeleteAsync("/providers/openai/models?model=nunca-habilitado");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"code\":\"model-not-enabled\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retiring_DoesNotChangeTheAgent()
    {
        await EnableDirectlyAsync("openai", "modelo-do-agente");
        var agentId = await InsertAgentAsync("openai", "modelo-do-agente", isActive: true);

        using var response = await _admin.DeleteAsync("/providers/openai/models?model=modelo-do-agente");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var agent = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
        Assert.Equal("openai", agent.Provider);
        Assert.Equal("modelo-do-agente", agent.Model);
    }

    // ------------------------------------------------------------- PAPEL

    [Fact]
    public async Task OnlyTheAdminCurates_AndEveryRoleReadsProviders()
    {
        await EnableDirectlyAsync("openai", "modelo-dos-papeis");

        foreach (var role in new[] { UserRole.Operator, UserRole.Reader })
        {
            var client = await ClientForRoleAsync(role);
            using var list = await client.GetAsync("/providers/models");
            using var retire = await client.DeleteAsync("/providers/openai/models?model=modelo-dos-papeis");
            using var providers = await client.GetAsync("/providers");

            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, retire.StatusCode);
            Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        }

        Assert.True(await IsEnabledAsync("openai", "modelo-dos-papeis"));
        using var adminList = await _admin.GetAsync("/providers/models");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
    }

    [Fact]
    public async Task ADemotedAdmin_WithTheTokenFromBefore_Is403_AndTheModelStays()
    {
        await EnableDirectlyAsync("openai", "modelo-do-rebaixado");
        var demoted = await SignedInUser.CreateAsync(factory, "rebaixado-curadoria");
        await LocalUserSeed.SetRoleAsync(factory.Services, demoted.User.Id, UserRole.Operator);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", demoted.AccessToken);

        using var response = await client.DeleteAsync("/providers/openai/models?model=modelo-do-rebaixado");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CurrentUser.ForbiddenMessage, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.True(await IsEnabledAsync("openai", "modelo-do-rebaixado"));
    }

    // ------------------------------------------------------------- APOIO

    private async Task<HttpClient> ClientForRoleAsync(UserRole role)
    {
        var user = await SignedInUser.CreateAsync(factory, $"papel-{role}".ToLowerInvariant(), role: role);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    private async Task<string> GetTextAsync(string path)
    {
        using var response = await _admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task EnableDirectlyAsync(string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await dbContext.EnabledLlmModels.AnyAsync(enabled => enabled.Provider == provider && enabled.Model == model))
        {
            dbContext.EnabledLlmModels.Add(new EnabledLlmModel(provider, model));
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<bool> IsEnabledAsync(string provider, string model) => await CountPairAsync(provider, model) > 0;

    private async Task<int> CountPairAsync(string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EnabledLlmModels
            .CountAsync(enabled => enabled.Provider == provider && enabled.Model == model);
    }

    private async Task<Guid> InsertAgentAsync(string provider, string model, bool isActive)
    {
        var id = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
            VALUES ({id}, 'Agente da curadoria', 'Instruções.', {isActive}, {provider}, {model}, now(), now())
            """);
        return id;
    }
}
