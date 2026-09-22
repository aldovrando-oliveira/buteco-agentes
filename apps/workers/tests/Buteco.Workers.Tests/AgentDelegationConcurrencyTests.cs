using System.Diagnostics;
using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.ExecutionMetrics;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        // Compostos com o LlmCallDurationChatClient real, como o
        // ChatClientResolver faz em produção — é ele quem registra as linhas
        // filhas que o guarda de contaminação lê.
        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = Measured(sourceChatClient.Object, SourceProvider, SourceModel),
            [(TargetProvider, TargetModel)] = Measured(targetChatClient.Object, TargetProvider, TargetModel),
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

            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, sourceTaskId);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        // ── Métricas (change metricas-execucao-coleta) ──────────────────
        // Origem na mesma linha do tempo de fila (V5 da exploração): é o que
        // deixa a tela separar a fila de task delegada — que mede topologia
        // de deploy — da fila de task externa.
        var sourceExecution = (await ExecutionMetricsReader.FindExecutionAsync(ConnectionString, sourceTaskId))!;
        Assert.Equal(ExecutionMetricsValues.Origin.External, sourceExecution.Origin);

        var outcome = Assert.Single(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, sourceTaskId));
        Assert.Equal(ExecutionMetricsValues.DelegationOutcome.Completed, outcome.Outcome);
        Assert.Equal(nameof(TaskState.Completed), outcome.LastObservedTargetState);
        Assert.NotNull(outcome.TargetTaskId);

        var targetExecution = await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, outcome.TargetTaskId!);
        Assert.Equal(targetId, targetExecution.AgentId);
        Assert.Equal(ExecutionMetricsValues.Origin.Delegation, targetExecution.Origin);
        Assert.Equal(sourceId, targetExecution.SourceAgentId);
        Assert.Equal(sourceTaskId, targetExecution.SourceTaskId);
        Assert.Equal(1, targetExecution.DelegationDepth);

        // Contaminação (o risco do AsyncLocal com duas execuções no mesmo
        // processo): cada task só tem as chamadas do SEU client, e o alvo não
        // aparece como origem de delegação nenhuma.
        var sourceCalls = await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, sourceTaskId);
        var targetCalls = await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, outcome.TargetTaskId!);
        Assert.NotEmpty(sourceCalls);
        Assert.All(sourceCalls, call => Assert.Equal(SourceProvider, call.Provider));
        Assert.NotEmpty(targetCalls);
        Assert.All(targetCalls, call => Assert.Equal(TargetProvider, call.Provider));
        Assert.Empty(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, outcome.TargetTaskId!));
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

    /// <summary>
    /// G1 — o par de G2. Instância única: a task do Target é publicada e
    /// <b>nunca consumida</b>, porque a única instância está ocupada esperando
    /// por ela. É a forma de contenção que o `C ≥ N` produz em produção, e o
    /// registro da desistência tem que dizer `Submitted`.
    /// </summary>
    [Fact]
    public async Task DelegationTimeout_WithTargetNeverConsumed_LogsLastObservedStateAsSubmitted()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, $"Fonte {sourceId:N}", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, $"Alvo {targetId:N}", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var sourceChatClient = BuildDelegationToolCallingChatClientMock(
            ExpectedToolName($"Alvo {targetId:N}"), "Uma tarefa qualquer.");

        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
        };

        var logs = new List<CapturedLogEntry>();
        using var singleInstance = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(3), delegationLogs: logs);
        await singleInstance.StartAsync();

        CapturedLogEntry giveUp;
        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            giveUp = await PollUntilDelegationGiveUpLoggedAsync(logs, sourceTaskId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, sourceTaskId);
        }
        finally
        {
            await singleInstance.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Submitted), giveUp.Value(LastObservedStateKey)?.ToString());
        Assert.Equal(sourceId, giveUp.Value(SourceAgentIdKey));
        Assert.Equal(targetId, giveUp.Value(TargetAgentIdKey));
        Assert.NotNull(giveUp.Value(TargetTaskIdKey));

        // Linha gêmea do registro (change metricas-execucao-coleta, D5): é esta
        // combinação — Expired com o alvo em Submitted — que torna o C da
        // replicas-de-worker consultável sem grep em log.
        var outcome = Assert.Single(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, sourceTaskId));
        Assert.Equal(ExecutionMetricsValues.DelegationOutcome.Expired, outcome.Outcome);
        Assert.Equal(nameof(TaskState.Submitted), outcome.LastObservedTargetState);
        Assert.Equal(targetId, outcome.TargetAgentId);
    }

    /// <summary>
    /// G2 — o par de G1, e é o par que prova que a distinção existe. Duas
    /// instâncias: a task do Target <b>é</b> consumida e fica em `Working`
    /// além do timeout do Source. Os dois guardas diferem pelo <b>valor do
    /// campo</b>, nunca pela redação da mensagem.
    /// </summary>
    [Fact]
    public async Task DelegationTimeout_WithTargetConsumedAndSlow_LogsLastObservedStateAsWorking()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, $"Fonte {sourceId:N}", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, $"Alvo {targetId:N}", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var sourceChatClient = BuildDelegationToolCallingChatClientMock(
            ExpectedToolName($"Alvo {targetId:N}"), "Uma tarefa qualquer.");

        // O Target segura a chamada ao LLM até ser liberado — é o que o mantém
        // em `Working` durante toda a espera do Source, e depois dela.
        var releaseTarget = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await releaseTarget.Task;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Resultado tardio do Target."));
            });

        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
            [(TargetProvider, TargetModel)] = targetChatClient.Object,
        };

        var logs = new List<CapturedLogEntry>();
        using var first = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(3), delegationLogs: logs);
        using var second = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(3), delegationLogs: logs);
        await first.StartAsync();
        await second.StartAsync();

        CapturedLogEntry giveUp;
        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            giveUp = await PollUntilDelegationGiveUpLoggedAsync(logs, sourceTaskId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, sourceTaskId);
        }
        finally
        {
            releaseTarget.TrySetResult();
            await first.StopAsync();
            await second.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Working), giveUp.Value(LastObservedStateKey)?.ToString());
        Assert.Equal(sourceId, giveUp.Value(SourceAgentIdKey));
        Assert.Equal(targetId, giveUp.Value(TargetAgentIdKey));

        var outcome = Assert.Single(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, sourceTaskId));
        Assert.Equal(ExecutionMetricsValues.DelegationOutcome.Expired, outcome.Outcome);
        Assert.Equal(nameof(TaskState.Working), outcome.LastObservedTargetState);
    }

    /// <summary>
    /// G3 — o ramo de estado terminal de falha, que é o que recebe o `Failed`
    /// por contenção de lock criado por `lock-de-contexto-falha-terminal`.
    /// Deste lado, esse `Failed` é <b>indistinguível</b> do `Failed` por erro de
    /// provedor (os dois passam pelo mesmo `FailTaskAsync`, que grava
    /// `Status.Message` nulo) — então o que este guarda prende não é
    /// classificação, é a <b>correlação</b>: os cinco identificadores que
    /// permitem achar o log que o próprio Target emitiu.
    /// </summary>
    [Fact]
    public async Task DelegationFailure_WithTargetInTerminalFailure_LogsCorrelationIdentifiers()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, $"Fonte {sourceId:N}", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, $"Alvo {targetId:N}", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var sourceChatClient = BuildDelegationToolCallingChatClientMock(
            ExpectedToolName($"Alvo {targetId:N}"), "Uma tarefa qualquer.");

        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provedor do Target indisponível."));

        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
            [(TargetProvider, TargetModel)] = targetChatClient.Object,
        };

        var logs = new List<CapturedLogEntry>();
        using var first = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(30), delegationLogs: logs);
        using var second = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(30), delegationLogs: logs);
        await first.StartAsync();
        await second.StartAsync();

        CapturedLogEntry giveUp;
        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            giveUp = await PollUntilDelegationGiveUpLoggedAsync(logs, sourceTaskId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, sourceTaskId);
        }
        finally
        {
            await first.StopAsync();
            await second.StopAsync();
        }

        Assert.Equal(sourceTaskId, giveUp.Value(SourceTaskIdKey));
        Assert.Equal(sourceId, giveUp.Value(SourceAgentIdKey));
        Assert.Equal(targetId, giveUp.Value(TargetAgentIdKey));
        Assert.NotNull(giveUp.Value(TargetTaskIdKey));
        Assert.Equal(nameof(TaskState.Failed), giveUp.Value(LastObservedStateKey)?.ToString());

        var outcome = Assert.Single(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, sourceTaskId));
        Assert.Equal(ExecutionMetricsValues.DelegationOutcome.TargetUnsuccessful, outcome.Outcome);
        Assert.Equal(nameof(TaskState.Failed), outcome.LastObservedTargetState);
        Assert.Equal(giveUp.Value(TargetTaskIdKey) as string, outcome.TargetTaskId);
    }

    /// <summary>
    /// G4 — a negativa de D1, com teste próprio para não sumir dentro de um
    /// positivo. Quando a espera é cancelada antes de <b>qualquer</b> leitura
    /// bem-sucedida, o registro NÃO apresenta um estado: ele declara que não
    /// houve observação. "Não sei" e "sei que não existe" são coisas
    /// diferentes, e gastar o vocabulário de um no outro apaga a distinção
    /// onde ela existe (convenção 13).
    /// </summary>
    [Fact]
    public async Task DelegationTimeout_WithNoSuccessfulRead_LogsAbsenceOfObservationInsteadOfState()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, $"Fonte {sourceId:N}", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, $"Alvo {targetId:N}", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var sourceChatClient = BuildDelegationToolCallingChatClientMock(
            ExpectedToolName($"Alvo {targetId:N}"), "Uma tarefa qualquer.");

        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient.Object,
        };

        // Timeout zero: o cancelamento da espera é agendado antes da primeira
        // leitura do store, então nenhuma leitura chega a concluir. É a forma
        // mais estável disponível — com um timeout pequeno mas positivo a
        // primeira leitura corre contra o cancelamento e o guarda ficaria
        // intermitente na própria asserção.
        var logs = new List<CapturedLogEntry>();
        using var singleInstance = BuildHost(clients, delegationTimeout: TimeSpan.Zero, delegationLogs: logs);
        await singleInstance.StartAsync();

        CapturedLogEntry giveUp;
        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            giveUp = await PollUntilDelegationGiveUpLoggedAsync(logs, sourceTaskId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, sourceTaskId);
        }
        finally
        {
            await singleInstance.StopAsync();
        }

        Assert.Equal(0, Convert.ToInt32(giveUp.Value(SuccessfulReadCountKey)));
        Assert.False(
            giveUp.HasKey(LastObservedStateKey),
            $"A desistência sem nenhuma leitura não deve apresentar um estado; mensagem emitida: '{giveUp.Message}'.");

        // A negativa também vale na linha: sem leitura, nenhum estado.
        var outcome = Assert.Single(await ExecutionMetricsReader.DelegationOutcomesAsync(ConnectionString, sourceTaskId));
        Assert.Equal(ExecutionMetricsValues.DelegationOutcome.Expired, outcome.Outcome);
        Assert.Null(outcome.LastObservedTargetState);
        Assert.Equal(0, outcome.SuccessfulReadCount);
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

    private IHost BuildHost(
        IReadOnlyDictionary<(string Provider, string Model), IChatClient> chatClientsByProviderModel,
        TimeSpan delegationTimeout,
        List<CapturedLogEntry>? delegationLogs = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        if (delegationLogs is not null)
        {
            builder.Logging.AddProvider(new CapturingLoggerProvider(delegationLogs, DelegationLoggerCategory));
        }

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

    private string ConnectionString => fixture.Postgres.GetConnectionString();

    private static IChatClient Measured(IChatClient inner, string provider, string model) =>
        new LlmCallDurationChatClient(inner, provider, model, NullLogger<LlmCallDurationChatClient>.Instance);

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    /// <summary>
    /// Devolve o registro de diagnóstico da desistência de uma delegação —
    /// aquele que carrega <c>SourceTaskId</c> — esperando até ele aparecer.
    /// </summary>
    /// <remarks>
    /// Casa pela <b>presença da chave estruturada</b>, nunca pelo texto da
    /// mensagem. É deliberado, e é o que separa este guarda dos que já
    /// enganaram nesta linha de trabalho: as duas changes anteriores
    /// (`lock-de-contexto-falha-terminal`, `delegacao-ciclo-no-cadastro`)
    /// mudaram a redação das mensagens desta classe e os estados terminais que
    /// as produzem, e um guarda escrito sobre "não concluiu dentro do timeout"
    /// reprovaria por texto — continuando vermelho depois da correção e dando a
    /// impressão de funcionar (convenção 15, segunda forma).
    /// </remarks>
    /// <remarks>
    /// <paramref name="maxAttempts"/> em 100 (≈20 s), mesmo orçamento de
    /// <c>PollUntilTerminalAsync</c> no teste de instância única desta classe.
    /// <b>Não é folga por precaução: os 50 iniciais reprovaram na suíte
    /// completa.</b> O sintoma foi inequívoco — "Chaves vistas:" saiu
    /// <b>vazio</b>, isto é, nada havia sido logado na categoria ainda, porque a
    /// task do Source nem tinha sido consumida. Sob a suíte inteira, com 13
    /// outras classes subindo e derrubando containers em sequência, start de host
    /// mais consumo do RabbitMQ não cabe em 10 s. A propriedade sob teste é
    /// <b>o que o registro diz</b>, nunca em quanto tempo ele aparece.
    /// </remarks>
    private static async Task<CapturedLogEntry> PollUntilDelegationGiveUpLoggedAsync(
        List<CapturedLogEntry> logs, string sourceTaskId, int maxAttempts = 100)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            lock (logs)
            {
                var entry = logs.Find(candidate => candidate.Value(SourceTaskIdKey) as string == sourceTaskId);
                if (entry is not null)
                {
                    return entry;
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Nenhum registro de desistência de delegação com {SourceTaskIdKey}='{sourceTaskId}' foi emitido a tempo. " +
            $"Chaves vistas: {string.Join(" | ", logs.Select(e => string.Join(",", e.State.Select(pair => pair.Key))))}");
    }

    private const string DelegationLoggerCategory = "Buteco.Workers.AgentDelegations";
    private const string SourceTaskIdKey = "SourceTaskId";
    private const string SourceAgentIdKey = "SourceAgentId";
    private const string TargetAgentIdKey = "TargetAgentId";
    private const string TargetTaskIdKey = "TargetTaskId";
    private const string LastObservedStateKey = "LastObservedTargetState";
    private const string SuccessfulReadCountKey = "SuccessfulReadCount";

    /// <summary>
    /// Captura o <b>estado estruturado</b> de cada registro (pares
    /// chave/valor), não só o texto formatado — é o estado que os guardas
    /// afirmam.
    /// </summary>
    /// <remarks>
    /// TERCEIRA cópia deste mecanismo nesta suíte: as outras duas estão em
    /// <c>TimeZoneStartupValidationTests</c> e
    /// <c>TemporalContextMessageInstantTests</c>, e capturam só nível e texto.
    /// Continua aninhada em vez de ir para <c>Support/</c> porque migrar as
    /// outras duas é mexer em dois arquivos fora do escopo desta change — mas o
    /// gatilho de extração da convenção 2 está cumprido, e está registrado
    /// como item aberto no <c>02-HISTORICO_E_STATUS.md</c>.
    ///
    /// <para>
    /// Filtra por categoria, e não é detalhe: sem o filtro, captura também o log
    /// de comando SQL do EF Core de <b>todo</b> teste desta classe, crescendo
    /// sem limite durante a vida da fixture e tornando <c>PollUntil</c> de
    /// outros testes flaky sob carga — medido em
    /// <c>inbox-sweep-service-resiliencia</c>.
    /// </para>
    /// </remarks>
    private sealed class CapturingLoggerProvider(List<CapturedLogEntry> entries, params string[] categoryPrefixes) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            Array.Exists(categoryPrefixes, prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
                ? new CapturingLogger(entries)
                : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<CapturedLogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var pairs = state is IReadOnlyList<KeyValuePair<string, object?>> structured
                    ? structured.ToList()
                    : [];

                // Os hosts destes testes logam de threads diferentes (duas
                // instâncias consumindo a mesma fila) — o lock é o que impede
                // corrupção da List durante a leitura do PollUntil.
                lock (entries)
                {
                    entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), pairs));
                }
            }
        }
    }

    private sealed record CapturedLogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State)
    {
        public object? Value(string key)
        {
            foreach (var pair in State)
            {
                if (pair.Key == key)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        public bool HasKey(string key)
        {
            foreach (var pair in State)
            {
                if (pair.Key == key)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
