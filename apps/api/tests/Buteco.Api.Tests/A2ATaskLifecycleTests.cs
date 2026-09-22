using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using global::A2A;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

        // Regressão: SendMessage sem configuration.pushNotificationConfig
        // continua publicando normalmente, sem nenhum config anexado.
        Assert.Null(publishedMessage.PushNotificationConfig);

        var getPayload = new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } };
        var getResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", getPayload);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(getBody.TryGetProperty("error", out _));
        Assert.Equal("TASK_STATE_SUBMITTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendMessage_WithPushNotificationConfig_PropagatesConfigInPublishedJobMessage()
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
                configuration = new
                {
                    pushNotificationConfig = new
                    {
                        url = "https://cliente.example.com/webhooks/a2a",
                        authentication = new { scheme = "Bearer", credentials = "auth-credential" },
                        token = "webhook-token",
                    },
                },
            },
        };

        var sendResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", sendPayload);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sendBody = await sendResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskId = sendBody.GetProperty("result").GetProperty("task").GetProperty("id").GetString()!;

        var publishedMessage = await ReadJobMessageFromQueueAsync();
        Assert.NotNull(publishedMessage);
        Assert.Equal(taskId, publishedMessage!.TaskId);
        Assert.NotNull(publishedMessage.PushNotificationConfig);
        Assert.Equal("https://cliente.example.com/webhooks/a2a", publishedMessage.PushNotificationConfig!.Url);
        Assert.Equal("Bearer", publishedMessage.PushNotificationConfig.Authentication?.Scheme);
        Assert.Equal("auth-credential", publishedMessage.PushNotificationConfig.Authentication?.Credentials);
        Assert.Equal("webhook-token", publishedMessage.PushNotificationConfig.Token);
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

    /// <summary>
    /// A PREMISSA da guarda de estado terminal de <c>apps/workers</c> (change
    /// <c>metricas-execucao-coleta</c>, D17), transformada em guarda: mensagem
    /// para uma task JÁ TERMINAL é recusada pelo protocolo e NÃO publica job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O worker passou a não executar task que lê em estado terminal. Isso só é
    /// seguro se nenhum caminho legítimo entregar a ele uma task terminal para
    /// executar — e o caminho que parecia poder fazê-lo é o de "continuação" de
    /// uma task concluída. Ele não existe: o <c>A2AServer</c> recusa a mensagem
    /// (<c>GuardTerminalState</c>, decompilado do A2A 1.0.0-preview2: "Task is in
    /// a terminal state and cannot accept messages") antes de o
    /// <c>EnqueueingAgentHandler</c> rodar. A conversa continua em task NOVA, no
    /// mesmo contexto — o par disso está em <c>TaskJobConsumerTests</c> do worker.
    /// </para>
    /// <para>
    /// Passa antes e depois da guarda, de propósito. Se uma versão nova do pacote
    /// deixar de recusar, ESTE teste reprova — e é o aviso de que a guarda do
    /// worker passou a poder engolir mensagem legítima em silêncio.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task SendMessage_ToATaskAlreadyTerminal_IsRefused_AndPublishesNoJob()
    {
        var agentId = await CreateAgentAsync();

        var first = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "primeira" } }, messageId = Guid.NewGuid().ToString("N") } },
        });
        var task = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("task");
        var taskId = task.GetProperty("id").GetString()!;
        var contextId = task.GetProperty("contextId").GetString()!;
        Assert.NotNull(await ReadJobMessageFromQueueAsync());

        await MarkTaskCompletedAsync(taskId);

        var continuation = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "continuação" } },
                    messageId = Guid.NewGuid().ToString("N"),
                    taskId,
                    contextId,
                },
            },
        });

        var body = await continuation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("error", out var error), $"Esperava recusa; resposta: {body}");
        Assert.Contains("terminal", error.GetProperty("message").GetString());
        Assert.Null(await ReadJobMessageFromQueueAsync());
    }

    private async Task MarkTaskCompletedAsync(string taskId)
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var record = await dbContext.A2ATasks.SingleAsync(t => t.TaskId == taskId);
        var agentTask = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        agentTask.Status = new global::A2A.TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow };

        record.Update(record.ContextId, TaskState.Completed.ToString(), agentTask.Status.Timestamp,
            JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions));
        await dbContext.SaveChangesAsync();
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/agents",
            new { name = "Atendente", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
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
