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
/// Cobre a change inbox-instante-mensagem, Tarefas 2.5-2.8: o worker extrai
/// <c>Message.Metadata["messageInstant"]</c> da última mensagem do usuário e
/// repassa para <see cref="TemporalContextBlockBuilder.Build"/> — mesmo
/// estilo de <c>ConversationHistoryTests.SecondMessageInSameContext_TemporalBlockReflectsSecondExecutionInstant_NotFirst</c>
/// (pipeline real via <see cref="TaskJobConsumer"/>/<see cref="AgentExecutionService"/>,
/// asserção contra o <see cref="ChatOptions"/> capturado pelo <see cref="IChatClient"/>
/// mockado, não contra os construtores chamados isoladamente).
/// </summary>
public class TemporalContextMessageInstantTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    [Fact]
    public async Task MessageInstantPresent_PrecedenceResolvesAgainstMessageInstant_NotProcessingInstant()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        var messageInstant = new DateTimeOffset(2026, 3, 10, 9, 58, 0, TimeSpan.FromHours(-3));

        var instructions = await CaptureInstructionsAsync(
            processingInstant, messageMetadata: BuildMessageInstantMetadata(messageInstant));

        Assert.Contains("Instante da mensagem", instructions);
        Assert.Contains("2026-03-10T09:58:00-03:00", instructions);
        Assert.Contains("instante da mensagem acima, não contra o instante de processamento", instructions);
    }

    [Theory]
    [MemberData(nameof(AbsentMessageInstantCases))]
    public async Task MessageInstantAbsentOrIllegible_CollapsesToProcessingInstantPrecedence(
        Dictionary<string, JsonElement>? messageMetadata)
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));

        var instructions = await CaptureInstructionsAsync(processingInstant, messageMetadata);

        Assert.Contains("se resolve contra o instante acima.", instructions);
        Assert.DoesNotContain("Instante da mensagem", instructions);
    }

    public static IEnumerable<object?[]> AbsentMessageInstantCases()
    {
        // Caso 1: Message.Metadata nulo (nenhum chamador de SendMessage
        // forneceu nada).
        yield return [null];

        // Caso 2: Metadata presente, mas sem a chave messageInstant (ex.
        // cliente A2A externo que usa Metadata para outra coisa).
        yield return [new Dictionary<string, JsonElement> { ["outraChave"] = JsonSerializer.SerializeToElement("valor") }];

        // Caso 3: chave presente, valor não é uma string ISO 8601
        // parseável — tratado como ausência, não como erro de task (ver
        // teste de log de aviso, MessageInstantIllegible_LogsWarning_WithTaskId).
        yield return [new Dictionary<string, JsonElement> { [MessageInstantCodec.MetadataKey] = JsonSerializer.SerializeToElement("não-é-uma-data") }];
    }

    [Fact]
    public async Task MessageInstantIllegible_LogsWarning_WithTaskId()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        var illegibleMetadata = new Dictionary<string, JsonElement>
        {
            [MessageInstantCodec.MetadataKey] = JsonSerializer.SerializeToElement("não-é-uma-data"),
        };

        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();

        await RunSingleTaskAsync(processingInstant, illegibleMetadata, taskId, logs);

        Assert.Contains(logs, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(taskId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GapAboveThreshold_IncludesGapLine_EndToEnd()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        // Acima do limiar de 5 minutos (TemporalContextBlockBuilder.StaleResponseThreshold).
        var messageInstant = processingInstant - TimeSpan.FromMinutes(30);

        var instructions = await CaptureInstructionsAsync(processingInstant, BuildMessageInstantMetadata(messageInstant));

        Assert.Contains("Defasagem", instructions);
    }

    [Fact]
    public async Task GapBelowThreshold_DoesNotIncludeGapLine_EndToEnd()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        // Abaixo do limiar de 5 minutos.
        var messageInstant = processingInstant - TimeSpan.FromMinutes(1);

        var instructions = await CaptureInstructionsAsync(processingInstant, BuildMessageInstantMetadata(messageInstant));

        Assert.DoesNotContain("Defasagem", instructions);
    }

    private static Dictionary<string, JsonElement> BuildMessageInstantMetadata(DateTimeOffset messageInstant) =>
        new() { [MessageInstantCodec.MetadataKey] = MessageInstantCodec.Encode(messageInstant) };

    private async Task<string> CaptureInstructionsAsync(DateTimeOffset processingInstant, Dictionary<string, JsonElement>? messageMetadata)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var logs = new List<(LogLevel Level, string Message)>();
        var capturedOptions = await RunSingleTaskAsync(processingInstant, messageMetadata, taskId, logs);

        Assert.NotNull(capturedOptions);
        return capturedOptions!.Instructions!;
    }

    private async Task<ChatOptions?> RunSingleTaskAsync(
        DateTimeOffset processingInstant, Dictionary<string, JsonElement>? messageMetadata, string taskId, List<(LogLevel Level, string Message)> logs)
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

        var timeProvider = new FakeTimeProvider(processingInstant.ToUniversalTime());
        timeProvider.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test-03:00", TimeSpan.FromHours(-3), "Test -03:00", "Test -03:00"));

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
    // TimeZoneStartupValidationTests.CapturingLoggerProvider, adaptado para
    // também capturar o LogLevel (necessário para filtrar por Warning).
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
