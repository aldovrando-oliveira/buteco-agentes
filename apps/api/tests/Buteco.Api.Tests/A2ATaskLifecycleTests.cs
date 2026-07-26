using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Messaging;
using Buteco.Api.Tests.Support;
using RabbitMQ.Client;

namespace Buteco.Api.Tests;

public class A2ATaskLifecycleTests(A2ATaskLifecycleFixture fixture) : IClassFixture<A2ATaskLifecycleFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task SendMessage_CreatesSubmittedTask_PublishesJobAndIsRetrievableViaGetTask()
    {
        var agentId = await CreateAgentAsync();

        var messageId = Guid.NewGuid().ToString("N");
        var sendPayload = new
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

        var sendResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", sendPayload);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sendBody = await sendResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(sendBody.TryGetProperty("error", out _));

        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        var contextId = taskElement.GetProperty("contextId").GetString()!;
        Assert.Equal("TASK_STATE_SUBMITTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        var publishedMessage = await ReadJobMessageFromQueueAsync();
        Assert.NotNull(publishedMessage);
        Assert.Equal(taskId, publishedMessage!.TaskId);
        Assert.Equal(agentId, publishedMessage.AgentId);
        Assert.Equal(contextId, publishedMessage.ContextId);

        var getPayload = new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } };
        var getResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", getPayload);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(getBody.TryGetProperty("error", out _));
        Assert.Equal("TASK_STATE_SUBMITTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendMessage_ForUnknownAgent_ReturnsA2AProtocolError()
    {
        var unknownAgentId = Guid.NewGuid();
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
                    parts = new[] { new { text = "oi" } },
                    messageId,
                },
            },
        };

        var response = await _client.PostAsJsonAsync($"/agents/{unknownAgentId}/a2a", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("error", out var error));
        Assert.Contains(unknownAgentId.ToString(), error.GetProperty("message").GetString());
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync("/agents", new { name = "Atendente", instructions = "Responda com simpatia." });
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<JsonElement>();
        return agent.GetProperty("id").GetGuid();
    }

    private async Task<TaskJobMessage?> ReadJobMessageFromQueueAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMq.Hostname,
            Port = fixture.RabbitMq.GetMappedPublicPort(5672),
            UserName = "buteco",
            Password = "buteco_test_password",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var result = await channel.BasicGetAsync(RabbitMqTaskJobPublisher.QueueName, autoAck: true);
            if (result is not null)
            {
                return JsonSerializer.Deserialize<TaskJobMessage>(result.Body.Span);
            }

            await Task.Delay(200);
        }

        return null;
    }
}
