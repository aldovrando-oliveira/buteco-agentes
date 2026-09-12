using System.Diagnostics;
using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Mcp;
using Buteco.Workers.Naming;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Prova direta da investigação bloqueante do design.md da change
/// apps-workers-delegacao-execucao (Decision 1): o consumidor RabbitMQ de
/// <c>apps/workers</c> roda com <c>prefetchCount: 1</c> — dentro de uma
/// única instância, uma tool de delegação que aguarda (`await`) a task do
/// Target terminar é um autodeadlock estrutural, porque a mesma instância
/// que está esperando é a única que poderia consumir a mensagem que ela
/// mesma está esperando terminar. Mesmo padrão de rigor do teste "sem
/// estado em memória entre instâncias" de
/// <c>ConversationHistoryTests</c>/apps-workers-historico-conversa: cada
/// instância aqui tem seu próprio <c>IServiceScopeFactory</c>/container
/// montado do zero, nenhum objeto compartilhado além do Postgres/RabbitMQ
/// do fixture.
///
/// As duas instâncias consomem da MESMA fila `agent-tasks`, sem afinidade
/// por agente — por isso são configuradas de forma IDÊNTICA, resolvendo o
/// <see cref="IChatClient"/> por <c>(provider, model)</c> exatamente como o
/// resolver real faz, nunca "esta instância só processa o Source".
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class AgentDelegationConcurrencyTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string SourceProvider = "openai";
    private const string SourceModel = "gpt-5.6-sol";
    private const string TargetProvider = "anthropic";
    private const string TargetModel = "claude-opus-5";

    [Fact]
    public async Task TwoInstances_ProcessSourceAndTargetConcurrently_DelegationSucceeds()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        var targetInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                targetInvoked.TrySetResult();
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Resultado do Target.")));
            });

        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
            [(TargetProvider, TargetModel)] = targetChatClient.Object,
        };

        // Instância A e instância B, configuradas de forma idêntica — cada
        // uma pode processar Source OU Target, o RabbitMQ decide.
        using var instanceA = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(30));
        using var instanceB = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(30));

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);

            // Prova direta: o mock do Target só pode ter sido invocado por
            // uma instância DIFERENTE da que processa o Source — a
            // instância ocupada com a task do Source está presa com sua
            // única mensagem não confirmada (prefetchCount: 1) durante
            // toda a espera da tool de delegação, então estruturalmente
            // não pode consumir a mensagem do Target ela mesma. Uma janela
            // curta (bem menor que os 30s configurados para o timeout da
            // delegação) descarta a possibilidade de isso ser "sucesso
            // eventual" por outro motivo em vez de consumo concorrente de
            // verdade por uma segunda instância.
            var invokedInTime = await Task.WhenAny(targetInvoked.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(targetInvoked.Task, invokedInTime);

            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);
            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);

            var finalText = ExtractArtifactText(sourceRecord);
            Assert.NotNull(finalText);
            Assert.Contains("Resultado do Target.", finalText);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }
    }

    [Fact]
    public async Task SingleInstance_DelegationTimesOutGracefully_DoesNotHangOrFailSourceTask()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        var shortTimeout = TimeSpan.FromSeconds(3);
        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
            // Nenhum client mapeado para TargetProvider/TargetModel — se a
            // task delegada fosse, contra a expectativa, processada por
            // esta mesma instância, a resolução do IChatClient explodiria
            // com KeyNotFoundException, o que tornaria o teste falho de um
            // jeito óbvio em vez de mascarar o cenário.
        };

        // Uma única instância ativa — a mesma que publica a task delegada
        // do Target é a única capaz de consumi-la, mas está ocupada
        // aguardando essa mesma task terminar. Autodeadlock estrutural
        // (design.md, Decision 1); só resolvido pelo timeout configurado.
        using var singleInstance = BuildHost(clients, delegationTimeout: shortTimeout);
        await singleInstance.StartAsync();

        var stopwatch = Stopwatch.StartNew();
        A2ATaskRecord sourceRecord;
        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            sourceRecord = await PollUntilTerminalAsync(sourceTaskId, maxAttempts: 100);
        }
        finally
        {
            await singleInstance.StopAsync();
        }

        stopwatch.Stop();

        // Levou pelo menos o timeout configurado — prova de que realmente
        // esperou até expirar, não que falhou rápido por outro motivo —
        // com folga para overhead do teste, sem se aproximar do timeout de
        // produção (120s).
        Assert.True(stopwatch.Elapsed >= shortTimeout, $"Esperado >= {shortTimeout}, levou {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Não deveria se aproximar do timeout de produção; levou {stopwatch.Elapsed}.");

        Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);

        // Não afirmamos que a task do Target "fica Submitted para
        // sempre": assim que a mensagem do Source é confirmada (Ack) —
        // só depois que o timeout interno expira e ExecuteAsync retorna —
        // a MESMA instância fica livre de novo e eventualmente consome a
        // mensagem do Target ainda na fila. A prova desta Decision está
        // na asserção de tempo decorrido acima (bloqueou até o timeout
        // configurado) e em TwoInstances_..., que mostra o caminho
        // positivo com uma segunda instância disponível.
    }

    private static string ExpectedToolName(string targetAgentName) =>
        ToolNameSanitizer.Sanitize($"delegate_to_{ToolNameSlugifier.Slugify(targetAgentName)}");

    private static string? ExtractArtifactText(A2ATaskRecord record)
    {
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        return task.Artifacts?.LastOrDefault()?.Parts.FirstOrDefault(part => part.Text is not null)?.Text;
    }

    private static Mock<IChatClient> BuildDelegationToolCallingChatClientMock(string toolName, string delegatedMessage)
    {
        var mock = new Mock<IChatClient>();
        var callIndex = 0;

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    var arguments = new Dictionary<string, object?> { ["message"] = delegatedMessage };
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        new List<AIContent> { new FunctionCallContent("call-1", toolName, arguments) })));
                }

                var toolResults = messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionResultContent>()
                    .Select(r => r.Result?.ToString())
                    .Where(text => text is not null);

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    $"Resposta final: {string.Join(", ", toolResults)}")));
            });

        return mock;
    }

    private IHost BuildHost(IReadOnlyDictionary<(string Provider, string Model), IChatClient> chatClientsByProviderModel, TimeSpan delegationTimeout)
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

        builder.Services.Configure<AgentDelegationToolOptions>(options =>
        {
            options.Timeout = delegationTimeout;
            options.PollInterval = TimeSpan.FromMilliseconds(200);
        });

        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string provider, string model) => chatClientsByProviderModel[(provider, model)]);
        builder.Services.AddSingleton(resolverMock.Object);
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, AgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<IKnowledgeToolSetResolver, NullKnowledgeToolSetResolver>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName, string provider, string model)
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {"Instruções de teste."}, {true}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task SeedAgentDelegationAsync(Guid sourceAgentId, Guid targetAgentId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_delegations ("SourceAgentId", "TargetAgentId")
             VALUES ({sourceAgentId}, {targetAgentId})
             """);
    }

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

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task<A2ATaskRecord> PollUntilTerminalAsync(string taskId, int maxAttempts = 50)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed) or nameof(TaskState.Rejected))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não atingiu um estado terminal a tempo.");
    }
}
