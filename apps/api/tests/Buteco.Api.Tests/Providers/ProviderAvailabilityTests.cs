using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Providers.Entities;
using Buteco.Api.RejectionMetrics;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Buteco.Api.Tests.Support.FakeVendorModelsHttpMessageHandler;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// A lista curada como fonte de <c>GET /providers</c>, da validação de agente e da recusa do
/// <c>SendMessage</c> (design.md da change modelos-curados-por-provedor, D2 e D7; specs
/// <c>llm-provider-catalog</c>, <c>agent-catalog</c>, <c>a2a-task-lifecycle</c> e
/// <c>agent-rejection-metrics</c>).
///
/// <para>
/// O publicador falso é um só para a classe: "não publicou" é sempre afirmado pelo
/// <c>taskId</c> da task, nunca pela fila vazia.
/// </para>
/// </summary>
public class ProviderAvailabilityTests(ProviderAvailabilityTests.Fixture factory) : IClassFixture<ProviderAvailabilityTests.Fixture>, IAsyncLifetime
{
    public sealed class Fixture : ModelCurationFixture;

    private readonly HttpClient _panel = factory.CreateClient();

    private readonly HttpClient _inbox = TestAuthentication.InboxServiceClient(factory);

    public Task InitializeAsync()
    {
        factory.ResetKeys();
        foreach (var provider in new[] { "openai", "anthropic", "gemini" })
        {
            factory.Vendor(provider).Reset(Always(HttpStatusCode.OK, "{}"));
        }

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        factory.ResetKeys();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- GET /providers (2.3)

    [Fact]
    public async Task AConfiguredProvider_BringsExactlyTheEnabledModels_InTheDatabaseOrder()
    {
        factory.SetKeys(anthropic: true);
        await EnableAsync("anthropic", "claude-extra-da-lista");

        var json = await _panel.GetStringAsync("/providers");

        using var document = JsonDocument.Parse(json);
        var anthropic = document.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetString() == "anthropic");
        var expected = await EnabledInDatabaseOrderAsync("anthropic");
        Assert.Equal(expected, anthropic.GetProperty("models").EnumerateArray().Select(m => m.GetString()!).ToList());
        Assert.Contains("claude-extra-da-lista", expected);
    }

    [Fact]
    public async Task AProviderWithoutKey_IsAbsent_EvenWithEnabledModels()
    {
        await EnableAsync("gemini", "gemini-sem-chave-na-lista");

        var json = await _panel.GetStringAsync("/providers");

        Assert.DoesNotContain("\"gemini\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConfiguredProviderWithoutEnabledModels_ComesWithAnEmptyList()
    {
        factory.SetKeys(gemini: true);
        var removed = await RemoveAllAsync("gemini");
        try
        {
            var json = await _panel.GetStringAsync("/providers");

            Assert.Contains("{\"id\":\"gemini\",\"models\":[]}", json, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var model in removed)
            {
                await EnableAsync("gemini", model);
            }
        }
    }

    [Fact]
    public async Task AModelEnabledAfterTheBoot_AppearsWithoutRestarting()
    {
        await EnableAsync("openai", "habilitado-depois-do-boot");

        var json = await _panel.GetStringAsync("/providers");

        Assert.Contains("habilitado-depois-do-boot", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModelEnabledOnlyForOpenAi_IsRefusedForAnthropic()
    {
        factory.SetKeys(anthropic: true);
        await EnableAsync("openai", "so-da-openai");

        using var response = await _panel.PostAsJsonAsync("/agents", new CreateAgentRequest("Atendente", "Instruções.", "anthropic", "so-da-openai"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"model\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- EDIÇÃO (2.4, R10)

    [Fact]
    public async Task UpdatingWhileKeepingARetiredModel_Is400OnModel_AndTheRecordStays()
    {
        await EnableAsync("openai", "modelo-retirado-na-edicao");
        var agentId = await CreateAgentAsync("openai", "modelo-retirado-na-edicao");
        await RetireAsync("openai", "modelo-retirado-na-edicao");

        using var response = await _panel.PutAsJsonAsync($"/agents/{agentId}", new UpdateAgentRequest("Nome Novo", "Instruções.", "openai", "modelo-retirado-na-edicao"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"model\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var agent = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
        Assert.Equal("Atendente", agent.Name);
    }

    [Fact]
    public async Task UpdatingFromARetiredModelToAnEnabledOne_AcceptsTasksAgain()
    {
        await EnableAsync("openai", "modelo-que-sai");
        var agentId = await CreateAgentAsync("openai", "modelo-que-sai");
        await RetireAsync("openai", "modelo-que-sai");

        using var update = await _panel.PutAsJsonAsync($"/agents/{agentId}", new UpdateAgentRequest("Atendente", "Instruções.", "openai", "gpt-5.6-sol"));
        var (state, taskId) = await SendMessageAsync(agentId);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("TASK_STATE_SUBMITTED", state);
        Assert.True(WasPublished(taskId));
    }

    // ------------------------------------------------------------- SendMessage (5.2, 5.3)

    [Fact]
    public async Task SendMessage_ForAgentWithRetiredModel_IsRejected_WithoutPublishing_AndGetTaskSaysRejected()
    {
        await EnableAsync("openai", "modelo-retirado-no-envio");
        var agentId = await CreateAgentAsync("openai", "modelo-retirado-no-envio");
        await RetireAsync("openai", "modelo-retirado-no-envio");

        var (state, taskId) = await SendMessageAsync(agentId);

        Assert.Equal("TASK_STATE_REJECTED", state);
        Assert.False(WasPublished(taskId));
        var get = await _inbox.PostAsJsonAsync($"/agents/{agentId}/a2a", new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } });
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TASK_STATE_REJECTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task EnablingAgain_WithTheA2AServerAlreadyResolved_AcceptsTheNextMessage()
    {
        await EnableAsync("openai", "modelo-volta");
        var agentId = await CreateAgentAsync("openai", "modelo-volta");
        await RetireAsync("openai", "modelo-volta");
        var (rejected, _) = await SendMessageAsync(agentId);

        await EnableAsync("openai", "modelo-volta");
        var (state, taskId) = await SendMessageAsync(agentId);

        Assert.Equal("TASK_STATE_REJECTED", rejected);
        Assert.Equal("TASK_STATE_SUBMITTED", state);
        Assert.True(WasPublished(taskId));
    }

    /// <summary>R6: os três fornecedores falham, e a mensagem não depende deles.</summary>
    [Fact]
    public async Task SendMessage_DoesNotCallAnyVendor()
    {
        foreach (var provider in new[] { "openai", "anthropic", "gemini" })
        {
            factory.Vendor(provider).Reset(Unreachable());
        }

        var agentId = await CreateAgentAsync("openai", "gpt-5.6-sol");
        var (state, taskId) = await SendMessageAsync(agentId);

        Assert.Equal("TASK_STATE_SUBMITTED", state);
        Assert.True(WasPublished(taskId));
        Assert.Equal(0, factory.VendorRequestCount);
    }

    [Fact]
    public async Task ARetiredModel_RecordsModelNotEnabled()
    {
        await EnableAsync("openai", "modelo-do-motivo");
        var agentId = await CreateAgentAsync("openai", "modelo-do-motivo");
        await RetireAsync("openai", "modelo-do-motivo");

        var (_, taskId) = await SendMessageAsync(agentId);

        Assert.Equal(RejectionMetricsValues.Reason.ModelNotEnabled, await RejectionReasonAsync(taskId));
    }

    [Fact]
    public async Task ProviderWithoutKey_AndModelNotEnabled_RecordsTheKeyReason()
    {
        factory.SetKeys(gemini: true);
        await EnableAsync("gemini", "gemini-dos-dois-motivos");
        var agentId = await CreateAgentAsync("gemini", "gemini-dos-dois-motivos");
        await RetireAsync("gemini", "gemini-dos-dois-motivos");
        factory.SetKeys(gemini: false);

        var (_, taskId) = await SendMessageAsync(agentId);

        Assert.Equal(RejectionMetricsValues.Reason.ProviderNotConfigured, await RejectionReasonAsync(taskId));
    }

    // ------------------------------------------------------------- O ACORDO (5.4, D7)

    public static TheoryData<string, string, string, bool, string?> AgreementCases() => new()
    {
        // provedor, modelo habilitado em, modelo pedido, chave do provedor, campo esperado do erro
        { "gemini", "gemini", "acordo-habilitado-com-chave", true, null },
        { "gemini", "gemini", "acordo-habilitado-sem-chave", false, "provider" },
        { "gemini", "nenhum", "acordo-nao-habilitado", true, "model" },
        { "gemini", "openai", "acordo-de-outro-provedor", true, "model" },
    };

    /// <summary>
    /// A validação de agente e o <c>SendMessage</c> são o mesmo método (D7): para cada caso, o
    /// <c>PUT</c> é aceito se e somente se o <c>SendMessage</c> é aceito, e o campo do erro de
    /// validação corresponde ao motivo da recusa.
    /// </summary>
    [Theory]
    [MemberData(nameof(AgreementCases))]
    public async Task AgentValidation_AndSendMessage_AgreeOnThePair(string provider, string enabledFor, string model, bool withKey, string? expectedField)
    {
        // Um agente válido de partida, editado depois para o par do caso.
        var agentId = await CreateAgentAsync("openai", "gpt-5.6-sol");
        factory.SetKeys(gemini: true);
        if (enabledFor != "nenhum")
        {
            await EnableAsync(enabledFor, model);
        }

        // O SendMessage lê o agente gravado: grava o par direto, sem passar pela validação.
        await SetAgentPairAsync(agentId, provider, model);
        factory.SetKeys(gemini: withKey);

        using var put = await _panel.PutAsJsonAsync($"/agents/{agentId}", new UpdateAgentRequest("Atendente", "Instruções.", provider, model));
        var (state, taskId) = await SendMessageAsync(agentId);

        var putAccepted = put.StatusCode == HttpStatusCode.OK;
        var sendAccepted = state == "TASK_STATE_SUBMITTED";
        Assert.Equal(putAccepted, sendAccepted);
        Assert.Equal(expectedField is null, putAccepted);
        if (expectedField is not null)
        {
            Assert.Contains($"\"{expectedField}\"", await put.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var expectedReason = expectedField == "provider"
                ? RejectionMetricsValues.Reason.ProviderNotConfigured
                : RejectionMetricsValues.Reason.ModelNotEnabled;
            Assert.Equal(expectedReason, await RejectionReasonAsync(taskId));
        }
    }

    // ------------------------------------------------------------- APOIO

    private async Task<Guid> CreateAgentAsync(string provider, string model)
    {
        var response = await _panel.PostAsJsonAsync("/agents", new CreateAgentRequest("Atendente", "Instruções.", provider, model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!.Id;
    }

    private async Task<(string State, string TaskId)> SendMessageAsync(Guid agentId)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "Olá" } }, messageId = Guid.NewGuid().ToString("N") } },
        };

        var response = await _inbox.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("task");
        return (task.GetProperty("status").GetProperty("state").GetString()!, task.GetProperty("id").GetString()!);
    }

    private bool WasPublished(string taskId) => factory.TaskJobPublisher.PublishedMessages.Any(message => message.TaskId == taskId);

    private async Task<string?> RejectionReasonAsync(string taskId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TaskRejections.AsNoTracking()
            .Where(rejection => rejection.TaskId == taskId).Select(rejection => rejection.Reason).SingleOrDefaultAsync();
    }

    private async Task EnableAsync(string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await dbContext.EnabledLlmModels.AnyAsync(enabled => enabled.Provider == provider && enabled.Model == model))
        {
            dbContext.EnabledLlmModels.Add(new EnabledLlmModel(provider, model));
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task RetireAsync(string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().EnabledLlmModels
            .Where(enabled => enabled.Provider == provider && enabled.Model == model).ExecuteDeleteAsync();
    }

    private async Task<List<string>> RemoveAllAsync(string provider)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var models = await dbContext.EnabledLlmModels.Where(enabled => enabled.Provider == provider).Select(enabled => enabled.Model).ToListAsync();
        await dbContext.EnabledLlmModels.Where(enabled => enabled.Provider == provider).ExecuteDeleteAsync();
        return models;
    }

    private async Task<List<string>> EnabledInDatabaseOrderAsync(string provider)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT \"Model\" AS \"Value\" FROM llm_enabled_models WHERE \"Provider\" = {provider} ORDER BY \"Model\"")
            .ToListAsync();
    }

    private async Task SetAgentPairAsync(Guid agentId, string provider, string model)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE agents SET \"Provider\" = {provider}, \"Model\" = {model} WHERE \"Id\" = {agentId}");
    }
}
