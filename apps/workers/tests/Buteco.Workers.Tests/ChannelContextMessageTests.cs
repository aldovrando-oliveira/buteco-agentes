using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change inbox-contexto-canal, Tarefas 2.5-2.6: o worker extrai
/// <c>Message.Metadata["channelType"]</c>/<c>["contactExternalId"]</c> da
/// última mensagem do usuário e repassa para
/// <see cref="ChannelContextBlockBuilder.Build"/> — mesmo estilo de
/// <see cref="TemporalContextMessageInstantTests"/> (pipeline real via
/// <see cref="TaskJobConsumer"/>/<see cref="AgentExecutionService"/>,
/// asserção contra o <see cref="ChatOptions"/> capturado pelo
/// <see cref="IChatClient"/> mockado).
/// </summary>
public class ChannelContextMessageTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    [Fact]
    public async Task BothFieldsPresent_InstructionsIncludeFullChannelContextBlock()
    {
        var metadata = BuildChannelContextMetadata(channelType: "waha", contactExternalId: "5511912345678@c.us");

        var instructions = await CaptureInstructionsAsync(metadata);

        Assert.Contains("Canal de origem desta conversa: waha", instructions);
        Assert.Contains("Identificador do contato atribuído pelo canal: 5511912345678@c.us", instructions);
    }

    [Fact]
    public async Task BothFieldsAbsent_InstructionsDoNotIncludeChannelContextBlock()
    {
        var instructions = await CaptureInstructionsAsync(messageMetadata: null);

        Assert.DoesNotContain("Contexto de canal", instructions);
        Assert.DoesNotContain("Canal de origem", instructions);
        Assert.DoesNotContain("Identificador do contato", instructions);
    }

    [Fact]
    public async Task OnlyChannelTypePresent_InstructionsIncludePartialBlockWithoutMentioningContactExternalId()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["channelType"] = JsonSerializer.SerializeToElement("telegram", A2AJsonUtilities.DefaultOptions),
        };

        var instructions = await CaptureInstructionsAsync(metadata);

        Assert.Contains("Canal de origem desta conversa: telegram", instructions);
        Assert.DoesNotContain("Identificador do contato", instructions);
    }

    [Fact]
    public async Task ChannelTypeWithNonStringType_TreatedAsAbsentAndLogsWarning()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();
        var metadata = new Dictionary<string, JsonElement>
        {
            ["channelType"] = JsonSerializer.SerializeToElement(42, A2AJsonUtilities.DefaultOptions),
        };

        var capturedOptions = await RunSingleTaskAsync(metadata, taskId, logs);

        Assert.DoesNotContain("Canal de origem", capturedOptions!.Instructions!);
        Assert.Contains(logs, entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(taskId, StringComparison.Ordinal)
            && entry.Message.Contains("channelType", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContactExternalIdWithNonStringType_TreatedAsAbsentAndLogsWarning()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();
        var metadata = new Dictionary<string, JsonElement>
        {
            ["contactExternalId"] = JsonSerializer.SerializeToElement(new { }, A2AJsonUtilities.DefaultOptions),
        };

        var capturedOptions = await RunSingleTaskAsync(metadata, taskId, logs);

        Assert.DoesNotContain("Identificador do contato", capturedOptions!.Instructions!);
        Assert.Contains(logs, entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(taskId, StringComparison.Ordinal)
            && entry.Message.Contains("contactExternalId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BothFieldsAbsent_DoesNotLogWarning_DistinguishingSilentAbsenceFromWrongType()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();

        await RunSingleTaskAsync(messageMetadata: null, taskId, logs);

        Assert.DoesNotContain(logs, entry => entry.Message.Contains("channelType", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, entry => entry.Message.Contains("contactExternalId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessingTask_DoesNotPersistChannelContextBlockIntoAgentInstructions()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");
        const string originalInstructions = "Responda com simpatia.";

        await SeedAgentAsync(agentId, "Atendente", originalInstructions);
        await SeedTaskAsync(taskId, agentId, contextId, "Tem lugar amanhã?", BuildChannelContextMetadata("waha", "5511912345678@c.us"));

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = BuildHost(chatClient.Object, timeProvider, []);
        await host.StartAsync();
        try
        {
            await PublishJobAsync(taskId, agentId, contextId);
            var record = await PollUntilTerminalAsync(taskId);
            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);
        var persistedInstructions = await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId)
            .Select(agent => agent.Instructions)
            .SingleAsync();

        Assert.Equal(originalInstructions, persistedInstructions);
    }

    private static Dictionary<string, JsonElement> BuildChannelContextMetadata(string? channelType, string? contactExternalId)
    {
        var metadata = new Dictionary<string, JsonElement>();

        if (channelType is not null)
        {
            metadata["channelType"] = JsonSerializer.SerializeToElement(channelType, A2AJsonUtilities.DefaultOptions);
        }

        if (contactExternalId is not null)
        {
            metadata["contactExternalId"] = JsonSerializer.SerializeToElement(contactExternalId, A2AJsonUtilities.DefaultOptions);
        }

        return metadata;
    }

    private async Task<string> CaptureInstructionsAsync(Dictionary<string, JsonElement>? messageMetadata)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();
        var capturedOptions = await RunSingleTaskAsync(messageMetadata, taskId, logs);

        Assert.NotNull(capturedOptions);
        return capturedOptions!.Instructions!;
    }

    private async Task<ChatOptions?> RunSingleTaskAsync(
        Dictionary<string, JsonElement>? messageMetadata, string taskId, List<(LogLevel Level, string Message)> logs)
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskAsync(taskId, agentId, contextId, "Tem lugar amanhã?", messageMetadata);

        ChatOptions? capturedOptions = null;
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, options, _) => capturedOptions = options)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var host = BuildHost(chatClient.Object, timeProvider, logs);
        await host.StartAsync();
        try
        {
            await PublishJobAsync(taskId, agentId, contextId);
            var record = await PollUntilTerminalAsync(taskId);
            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }

        return capturedOptions;
    }

    private IHost BuildHost(IChatClient chatClient, TimeProvider timeProvider, List<(LogLevel Level, string Message)> logs)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Logging.AddProvider(new CapturingLoggerProvider(logs));

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
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton(timeProvider);
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName, string instructions, string provider = "openai", string model = "gpt-5.6-sol")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task SeedTaskAsync(
        string taskId, Guid agentId, string contextId, string userMessage, Dictionary<string, JsonElement>? messageMetadata)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

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
                    Metadata = messageMetadata,
                },
            ],
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, nameof(TaskState.Submitted), now, payload));
        await dbContext.SaveChangesAsync();
    }

    private async Task PublishJobAsync(string taskId, Guid agentId, string contextId)
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

        var message = new TaskJobMessage(taskId, agentId, contextId);
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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var dbContext = new AppDbContext(options);
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não atingiu um estado terminal a tempo.");
    }

    // Duplicado localmente (convenção 7) — mesmo mecanismo de
    // TemporalContextMessageInstantTests.CapturingLoggerProvider.
    private sealed class CapturingLoggerProvider(List<(LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
