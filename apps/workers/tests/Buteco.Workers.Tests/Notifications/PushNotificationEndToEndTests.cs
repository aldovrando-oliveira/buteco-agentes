using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Notifications.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests.Notifications;

/// <summary>
/// Cobre a change a2a-push-notifications fim a fim: pipeline real
/// (RabbitMQ + Postgres + <see cref="TaskJobConsumer"/> +
/// <see cref="AgentExecutionService"/>) com um "webhook do cliente" fake
/// recebendo (ou não) a chamada de notificação — ver design.md, Decisions
/// 1/2/3/6 e specs/a2a-push-notifications/spec.md.
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class PushNotificationEndToEndTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string WebhookUrl = "https://cliente.example.com/webhooks/a2a";

    [Fact]
    public async Task RoundTrip_TaskCompletedWithPushConfig_MetadataPersistedAndFakeReceivesExpectedPayload()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "Olá, tudo bem?");

        var chatClient = BuildSuccessfulChatClientMock("Tudo ótimo, e você?");
        var config = new PushNotificationConfig { Url = WebhookUrl };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);

        var persistedTask = DeserializeTask(record);
        Assert.NotNull(persistedTask.Metadata);
        Assert.True(persistedTask.Metadata!.TryGetValue(PushNotificationConfigCodec.MetadataKey, out var pushConfigElement));
        // Formato de fio da spec A2A: camelCase, opcionais ausentes omitidos
        // (nunca gravados como propriedade com valor null) — não
        // "consertar" de volta para "Url"/nulls se isto ficar vermelho no
        // futuro; é contrato, não coincidência (design.md de
        // push-notification-config-codec-encoder, Decisões D1/D5).
        Assert.Equal(WebhookUrl, pushConfigElement.GetProperty("url").GetString());
        AssertPushConfigOptionalFieldsAbsent(pushConfigElement, "Url", "Id", "id", "Authentication", "authentication", "Token", "token");

        var call = Assert.Single(handler.Calls);
        Assert.Equal(new Uri(WebhookUrl), call.Url);
        Assert.Equal(HttpMethod.Post, call.Method);

        var payload = JsonSerializer.Deserialize<AgentTask>(call.Body, A2AJsonUtilities.DefaultOptions)!;
        Assert.Equal(taskId, payload.Id);
        Assert.Equal(nameof(TaskState.Completed), payload.Status.State.ToString());
        var artifact = Assert.Single(payload.Artifacts!);
        Assert.Contains("Tudo ótimo", artifact.Parts[0].Text);
    }

    [Fact]
    public async Task TaskFailedWithPushConfig_MetadataPersistedAndFakeReceivesFailedPayload()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "pergunta que falha");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM indisponível"));

        var config = new PushNotificationConfig { Url = WebhookUrl };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Failed), record.State);

        var persistedTask = DeserializeTask(record);
        Assert.NotNull(persistedTask.Metadata);
        Assert.True(persistedTask.Metadata!.TryGetValue(PushNotificationConfigCodec.MetadataKey, out var pushConfigElement));
        // Formato de fio da spec A2A: camelCase, opcionais ausentes omitidos
        // (nunca gravados como propriedade com valor null) — não
        // "consertar" de volta para "Url"/nulls se isto ficar vermelho no
        // futuro; é contrato, não coincidência (design.md de
        // push-notification-config-codec-encoder, Decisões D1/D5).
        Assert.Equal(WebhookUrl, pushConfigElement.GetProperty("url").GetString());
        AssertPushConfigOptionalFieldsAbsent(pushConfigElement, "Url", "Id", "id", "Authentication", "authentication", "Token", "token");

        var call = Assert.Single(handler.Calls);
        var payload = JsonSerializer.Deserialize<AgentTask>(call.Body, A2AJsonUtilities.DefaultOptions)!;
        Assert.Equal(nameof(TaskState.Failed), payload.Status.State.ToString());
    }

    [Fact]
    public async Task TaskCompletedWithoutPushConfig_NeverCallsWebhook()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "sem push config");

        var chatClient = BuildSuccessfulChatClientMock("ok");

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, pushNotificationConfig: null);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.Empty(handler.Calls);

        var persistedTask = DeserializeTask(record);
        Assert.False(persistedTask.Metadata?.ContainsKey(PushNotificationConfigCodec.MetadataKey) ?? false);
    }

    [Fact]
    public async Task WebhookRespondsWith404_TaskStillCompletesNormally()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler { ResponseStatusCode = System.Net.HttpStatusCode.NotFound };

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "webhook 404");

        var chatClient = BuildSuccessfulChatClientMock("ok");
        var config = new PushNotificationConfig { Url = WebhookUrl };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task WebhookTimesOut_TaskStillCompletesWithoutBeingBlocked()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler { SimulateTimeout = true };

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "webhook lento");

        var chatClient = BuildSuccessfulChatClientMock("ok");
        var config = new PushNotificationConfig { Url = WebhookUrl };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            // Task já está completed no store bem antes de qualquer timeout
            // real de 5s (design.md, Decision 3) — PollUntilTerminalAsync
            // usa o mesmo timeout de teste das outras suítes.
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task AuthenticationPresent_ResultsInAuthorizationHeader()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "com authentication");

        var chatClient = BuildSuccessfulChatClientMock("ok");
        var config = new PushNotificationConfig
        {
            Url = WebhookUrl,
            Authentication = new AuthenticationInfo { Scheme = "Bearer", Credentials = "auth-credential" },
        };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        var call = Assert.Single(handler.Calls);
        var authHeader = Assert.Single(call.Headers["Authorization"]);
        Assert.Equal("Bearer auth-credential", authHeader);
        Assert.False(call.Headers.ContainsKey("X-A2A-Notification-Token"));

        // O aninhamento (authentication.scheme/credentials) é a parte mais
        // propensa a regredir — depende da naming policy se aplicar em
        // profundidade, não só no nível raiz do objeto (design.md de
        // push-notification-config-codec-encoder, Decisão D5).
        var persistedTask = DeserializeTask(record);
        Assert.True(persistedTask.Metadata!.TryGetValue(PushNotificationConfigCodec.MetadataKey, out var pushConfigElement));
        var authenticationElement = pushConfigElement.GetProperty("authentication");
        Assert.Equal("Bearer", authenticationElement.GetProperty("scheme").GetString());
        Assert.Equal("auth-credential", authenticationElement.GetProperty("credentials").GetString());
        AssertPushConfigOptionalFieldsAbsent(authenticationElement, "Scheme", "Credentials");
        AssertPushConfigOptionalFieldsAbsent(pushConfigElement, "Authentication", "Id", "id", "Token", "token");
    }

    [Fact]
    public async Task TokenPresent_ResultsInNotificationTokenHeader()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "com token");

        var chatClient = BuildSuccessfulChatClientMock("ok");
        var config = new PushNotificationConfig { Url = WebhookUrl, Token = "webhook-token" };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            record = await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        var call = Assert.Single(handler.Calls);
        var tokenHeader = Assert.Single(call.Headers["X-A2A-Notification-Token"]);
        Assert.Equal("webhook-token", tokenHeader);
        Assert.False(call.Headers.ContainsKey("Authorization"));

        var persistedTask = DeserializeTask(record);
        Assert.True(persistedTask.Metadata!.TryGetValue(PushNotificationConfigCodec.MetadataKey, out var pushConfigElement));
        Assert.Equal("webhook-token", pushConfigElement.GetProperty("token").GetString());
        AssertPushConfigOptionalFieldsAbsent(pushConfigElement, "Token", "Id", "id", "Authentication", "authentication");
    }

    [Fact]
    public async Task NoAuthenticationOrToken_NeitherHeaderIsSent()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        var handler = new FakeWebhookHttpMessageHandler();

        await SeedAgentAsync(agentId);
        await SeedTaskAsync(taskId, agentId, contextId, "sem auth nem token");

        var chatClient = BuildSuccessfulChatClientMock("ok");
        var config = new PushNotificationConfig { Url = WebhookUrl };

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId, config);
            await PollUntilTerminalAsync(taskId);
        }
        finally
        {
            await host.StopAsync();
        }

        var call = Assert.Single(handler.Calls);
        Assert.False(call.Headers.ContainsKey("Authorization"));
        Assert.False(call.Headers.ContainsKey("X-A2A-Notification-Token"));
    }

    private static Mock<IChatClient> BuildSuccessfulChatClientMock(string responseText)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        return mock;
    }

    private static AgentTask DeserializeTask(A2ATaskRecord record) =>
        JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;

    /// <summary>
    /// Afirma que nenhuma das propriedades nomeadas existe no elemento —
    /// usado para o casing antigo (PascalCase) e para campos opcionais
    /// ausentes, que a spec A2A exige omitidos, nunca gravados como
    /// propriedade com valor null (design.md de
    /// push-notification-config-codec-encoder, Decisão D5).
    /// </summary>
    private static void AssertPushConfigOptionalFieldsAbsent(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            Assert.False(
                element.TryGetProperty(propertyName, out _),
                $"'{propertyName}' não deveria existir — formato de fio A2A é camelCase e omite opcionais ausentes.");
        }
    }

    private IHost BuildHost(IChatClient chatClient, FakeWebhookHttpMessageHandler webhookHandler)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = fixture.RabbitMq.Hostname;
            options.Port = fixture.RabbitMq.GetMappedPublicPort(5672);
            options.Username = "buteco";
            options.Password = "buteco_test_password";
        });

        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock.Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>())).Returns(chatClient);
        builder.Services.AddSingleton(resolverMock.Object);
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<IKnowledgeToolSetResolver, NullKnowledgeToolSetResolver>();
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => webhookHandler);
        builder.Services.AddSingleton<PushNotificationSender>();

        // AgentExecutionService/TaskJobConsumer passaram a exigir TimeProvider
        // (change apps-workers-contexto-temporal).
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName = "Atendente", string instructions = "Responda com simpatia.", string provider = "openai", string model = "gpt-5.6-sol")
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {provider}, {model}, {now}, {now})
             """);
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedTaskAsync(string taskId, Guid agentId, string contextId, string userMessage)
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = now },
            History =
            [
                new Message
                {
                    Role = Role.User,
                    Parts = [Part.FromText(userMessage)],
                    MessageId = Guid.NewGuid().ToString("N"),
                    ContextId = contextId,
                },
            ],
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, nameof(TaskState.Submitted), now, payload));
        await dbContext.SaveChangesAsync();
    }

    private async Task PublishJobAsync(string taskId, Guid agentId, string contextId, PushNotificationConfig? pushNotificationConfig)
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
        await channel.QueueDeclareAsync(TaskJobConsumer.QueueName, durable: true, exclusive: false, autoDelete: false);

        var message = new TaskJobMessage(taskId, agentId, contextId, pushNotificationConfig);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: TaskJobConsumer.QueueName,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: body);
    }

    private async Task<A2ATaskRecord> PollUntilTerminalAsync(string taskId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não atingiu um estado terminal a tempo.");
    }
}
