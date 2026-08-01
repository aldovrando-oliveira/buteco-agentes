using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class SendMessageProviderRejectionTests(AgentProviderRejectionFixture fixture) : IClassFixture<AgentProviderRejectionFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task SendMessage_ForAgentWithoutProviderOrModel_RejectsTaskWithoutPublishing()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var sendBody = await SendMessageAsync(agentId);

        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        Assert.Equal("TASK_STATE_REJECTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        Assert.Empty(fixture.TaskJobPublisher.PublishedMessages);

        var getPayload = new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } };
        var getResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", getPayload);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(getBody.TryGetProperty("error", out _));
        Assert.Equal("TASK_STATE_REJECTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendMessage_ForAgentWithProviderThatBecameUnconfigured_RejectsTaskWithoutPublishing()
    {
        var configuration = fixture.Services.GetRequiredService<IConfiguration>();

        // Gemini configurado no momento do cadastro do agente.
        configuration["Gemini:ApiKey"] = "some-key";
        var agentId = await CreateAgentAsync("gemini", "gemini-3.6-flash");

        // Chave removida do ambiente entre o cadastro e o SendMessage.
        configuration["Gemini:ApiKey"] = null;

        var sendBody = await SendMessageAsync(agentId);

        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        Assert.Equal("TASK_STATE_REJECTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        Assert.Empty(fixture.TaskJobPublisher.PublishedMessages);
    }

    private async Task<JsonElement> SendMessageAsync(Guid agentId)
    {
        var messageId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "Olá, tudo bem?" } },
                    messageId,
                },
            },
        };

        var response = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateAgentAsync(string provider, string model)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest("Atendente", "Instruções.", provider, model));
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        return agent!.Id;
    }

    private async Task<Guid> SeedLegacyAgentWithoutProviderOrModelAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado"}, {"Instruções legadas."}, {true}, {now}, {now})
             """);

        return agentId;
    }
}
