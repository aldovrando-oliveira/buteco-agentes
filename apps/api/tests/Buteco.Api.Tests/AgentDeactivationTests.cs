using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentDeactivationTests(AgentDeactivationFixture fixture) : IClassFixture<AgentDeactivationFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task SendMessage_ForInactiveAgent_RejectsTaskWithoutPublishing()
    {
        var agentId = await CreateAgentAsync();
        await DeactivateAgentAsync(agentId);

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
    public async Task SendMessage_AfterReactivation_PublishesNormally()
    {
        var agentId = await CreateAgentAsync();
        await DeactivateAgentAsync(agentId);

        var rejectedBody = await SendMessageAsync(agentId);
        Assert.Equal(
            "TASK_STATE_REJECTED",
            rejectedBody.GetProperty("result").GetProperty("task").GetProperty("status").GetProperty("state").GetString());
        Assert.Empty(fixture.TaskJobPublisher.PublishedMessages);

        await ActivateAgentAsync(agentId);

        var submittedBody = await SendMessageAsync(agentId);
        var taskElement = submittedBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        Assert.Equal("TASK_STATE_SUBMITTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        var published = Assert.Single(fixture.TaskJobPublisher.PublishedMessages);
        Assert.Equal(taskId, published.TaskId);
        Assert.Equal(agentId, published.AgentId);
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

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync("/agents", new { name = "Atendente", instructions = "Responda com simpatia." });
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<JsonElement>();
        return agent.GetProperty("id").GetGuid();
    }

    private async Task DeactivateAgentAsync(Guid agentId)
    {
        var response = await _client.PostAsync($"/agents/{agentId}/deactivate", content: null);
        response.EnsureSuccessStatusCode();
    }

    private async Task ActivateAgentAsync(Guid agentId)
    {
        var response = await _client.PostAsync($"/agents/{agentId}/activate", content: null);
        response.EnsureSuccessStatusCode();
    }
}
