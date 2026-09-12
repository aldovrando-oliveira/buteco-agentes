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
using Microsoft.Extensions.Time.Testing;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-delegacao-execucao: execução real de
/// delegação entre agentes durante o processamento de uma task. Mesmo
/// estilo de <c>McpToolExecutionEndToEndTests</c> — pipeline real
/// (RabbitMQ + Postgres + <see cref="TaskJobConsumer"/> +
/// <see cref="AgentExecutionService"/>) com a tool de delegação real
/// (<see cref="AgentDelegationToolSetResolver"/>) sendo de fato chamada
/// pelo <c>FunctionInvokingChatClient</c> que o <c>ChatClientAgent</c>
/// insere automaticamente.
///
/// A maioria dos cenários usa DUAS instâncias de host (Source e Target) —
/// não por escolha de estilo, mas porque uma única instância nunca
/// consegue completar uma delegação de verdade (design.md, Decision 1:
/// `prefetchCount: 1` faz a instância que aguarda a task do Target ser a
/// mesma que precisaria consumi-la). Só os cenários em que a tool retorna
/// falha ANTES de publicar qualquer mensagem para o Target (Target
/// inválido, Source inválido) dispensam a segunda instância.
///
/// Importante: as duas instâncias consomem da MESMA fila `agent-tasks` —
/// não há afinidade de mensagem por agente, o RabbitMQ entrega para
/// QUALQUER instância livre (mesma razão da Decision 1: nenhuma noção de
/// "mesmo agentId → mesma instância"). Por isso as duas instâncias são
/// configuradas de forma IDÊNTICA — o <see cref="IChatClientResolver"/>
/// resolve por <c>(provider, model)</c>, exatamente como o resolver real,
/// nunca "esta instância sempre processa o Source". Dar cada instância um
/// mock fixo, como se ela só fosse processar um agente específico, é o
/// oposto do que a arquitetura garante e produz falhas espúrias quando o
/// RabbitMQ entrega a mensagem errada para a instância "errada".
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class AgentDelegationExecutionTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string SourceProvider = "openai";
    private const string SourceModel = "gpt-5.6-sol";
    private const string TargetProvider = "anthropic";
    private const string TargetModel = "claude-opus-5";

    [Fact]
    public async Task RoundTrip_ToolCallDecidedByLlm_DelegatesToTarget_AndResultReachesSource()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Preciso de um cálculo financeiro complexo.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Quanto é 2 + 2, considerando juros?");

        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "O resultado é 4.")));

        var clients = ClientsByProviderModel(sourceChatClient.Object, targetChatClient.Object);

        using var instanceA = BuildHost(clients);
        using var instanceB = BuildHost(clients);

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
            var finalText = ExtractArtifactText(sourceRecord);
            Assert.NotNull(finalText);
            Assert.Contains("O resultado é 4.", finalText);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Equal(nameof(TaskState.Completed), targetRecord!.State);
    }

    [Fact]
    public async Task DelegationDepthExceeded_TreatedAsToolFailure_SourceTaskCompletesNormally()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);

        // Profundidade já no teto (DelegationDepthLimit = 5 em
        // AgentExecutionService) — a task criada pela tool para o Target
        // nasce com profundidade 6, acima do teto.
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue mais uma vez, por favor.", delegationDepth: 5);

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");
        var targetChatClient = new Mock<IChatClient>();

        var clients = ClientsByProviderModel(sourceChatClient.Object, targetChatClient.Object);

        using var instanceA = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20));
        using var instanceB = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20));

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            // A task do Source segue seu fluxo normal — a profundidade
            // excedida é tratada como falha da tool, não como falha da
            // task de quem tentou delegar.
            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        // Prova direta: a task do Target foi rejeitada (não processada
        // pelo LLM) por exceder o teto — não "failed", ver design.md,
        // Decision 6.
        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Equal(nameof(TaskState.Rejected), targetRecord!.State);

        targetChatClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TargetActiveButMissingProviderModel_DegradesGracefully_NoTaskCreatedForTarget()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", provider: null, model: null);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        // Target nunca chega a ter uma task criada (checagem acontece
        // antes de publicar qualquer mensagem) — uma única instância
        // basta.
        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.Null(targetRecord);
    }

    [Fact]
    public async Task TargetInactiveButProviderModelConfigured_DegradesGracefully_NoTaskCreatedForTarget()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel, isActive: false);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Delegue, por favor.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.Null(targetRecord);
    }

    [Fact]
    public async Task SourceDeactivatedDuringOwnProcessing_DegradesGracefully_NoTaskCreatedForTarget()
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

        // O próprio primeiro GetResponseAsync (o que decide chamar a tool)
        // desativa o Source no banco como efeito colateral — simula a
        // desativação acontecendo DEPOIS que AgentExecutionService.ExecuteAsync
        // já carregou o Agent original (ativo), mas ANTES da tool
        // efetivamente rodar. Só é detectável porque a tool relê o Source
        // fresco do banco (Decision 5, corrigida durante a implementação —
        // ver design.md).
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(
            toolName,
            "Uma tarefa qualquer.",
            beforeFirstCall: () => DeactivateAgentAsync(sourceId).GetAwaiter().GetResult());

        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.Null(targetRecord);
    }

    [Fact]
    public async Task TimeoutExpires_WithoutTargetCompleting_SourceTaskDoesNotFail()
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

        // Só uma instância é iniciada — sem uma segunda instância para
        // consumir a task delegada, ela nunca é processada (mesma
        // mecânica da Decision 1). Timeout curto para o teste não levar
        // os 120s de produção.
        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null), delegationTimeout: TimeSpan.FromSeconds(3));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId, maxAttempts: 100);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
            var finalText = ExtractArtifactText(sourceRecord);
            Assert.NotNull(finalText);
            Assert.Contains("tempo limite", finalText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await host.StopAsync();
        }

        // Não afirmamos que a task do Target "fica Submitted para
        // sempre": uma vez que a mensagem do Source é finalmente
        // confirmada (Ack) — o que só acontece depois que o timeout
        // interno expira e ExecuteAsync retorna — a MESMA instância fica
        // livre de novo e eventualmente consome a mensagem do Target
        // ainda na fila (aqui ela nem tem um client mapeado para
        // Target/Provider, então explodiria com falha em vez de
        // silenciosamente "funcionar"). O que este teste prova é mais
        // restrito e já suficiente: o timeout expira de fato (ver
        // asserção de tempo decorrido) e a task do Source não falha por
        // causa disso — não que o Target nunca é tocado por essa mesma
        // instância depois que ela se libera.
    }

    [Fact]
    public async Task TargetTaskFails_TreatedAsToolFailure_SourceTaskCompletesNormally()
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

        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM do Target indisponível"));

        var clients = ClientsByProviderModel(sourceChatClient.Object, targetChatClient.Object);

        using var instanceA = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20));
        using var instanceB = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20));

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);

            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Equal(nameof(TaskState.Failed), targetRecord!.State);
    }

    [Fact]
    public async Task AgentWithoutAnyDelegation_ReceivesNoDelegationTool()
    {
        var sourceId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Solo", SourceProvider, SourceModel);
        await SeedTaskAsync(taskId, sourceId, contextId, "Oi, tudo bem?");

        ChatOptions? capturedOptions = null;
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, options, _) => capturedOptions = options)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tudo bem, e você?")));

        using var host = BuildHost(ClientsByProviderModel(chatClient.Object, null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, sourceId, contextId);
            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.NotNull(capturedOptions);
        Assert.DoesNotContain(capturedOptions!.Tools ?? [], tool => tool.Name.StartsWith("delegate_to_", StringComparison.Ordinal));
    }

    /// <summary>
    /// Cobre a change inbox-instante-mensagem, Tarefa 3.6: a task criada
    /// para o Target herda o mesmo `messageInstant` que o Source tinha
    /// disponível — checado direto no `Message.Metadata` persistido da task
    /// do Target, sem precisar que ela seja processada (mesmo padrão de
    /// <see cref="TimeoutExpires_WithoutTargetCompleting_SourceTaskDoesNotFail"/>,
    /// uma instância só, o Target nasce mas nunca é consumido).
    /// </summary>
    [Fact]
    public async Task DelegatedTask_WithMessageInstantOnSource_CarriesSameMessageInstantToTarget()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);

        var messageInstant = new DateTimeOffset(2026, 3, 10, 9, 58, 0, TimeSpan.FromHours(-3));
        await SeedTaskAsync(
            sourceTaskId, sourceId, contextId, "Preciso de um cálculo financeiro complexo.",
            messageMetadata: BuildMessageInstantMetadata(messageInstant));

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null), delegationTimeout: TimeSpan.FromSeconds(3));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);
            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Equal(messageInstant, ExtractMessageInstantFromHistory(targetRecord!));
    }

    /// <summary>Contraparte "sem item" da Tarefa 3.6 — sem messageInstant no Source, o Target não inventa um valor.</summary>
    [Fact]
    public async Task DelegatedTask_WithoutMessageInstantOnSource_DoesNotInventOneForTarget()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);
        await SeedTaskAsync(sourceTaskId, sourceId, contextId, "Preciso de um cálculo financeiro complexo.");

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Uma tarefa qualquer.");

        using var host = BuildHost(ClientsByProviderModel(sourceChatClient.Object, null), delegationTimeout: TimeSpan.FromSeconds(3));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);
            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await host.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Null(ExtractMessageInstantFromHistory(targetRecord!));
    }

    /// <summary>
    /// Cobre a Tarefa 3.8 — a contraparte que prova a Decisão D3 do
    /// design.md de ponta a ponta: Source e Target processados por
    /// instâncias com <see cref="TimeProvider"/> diferentes (instantes de
    /// processamento T1 ≠ T2, gap real, mesma mecânica de duas instâncias já
    /// documentada na classe), mas ambos recebem o MESMO `messageInstant` —
    /// sem este teste, a Tarefa 3.6 sozinha só prova que a chave foi
    /// escrita, não que ela sobrevive a um cenário com defasagem real de
    /// relógio entre Source e Target (ver invariante nomeado na Decisão D3).
    /// </summary>
    [Fact]
    public async Task SourceAndTarget_ProcessedAtDifferentInstants_BothReceiveSameMessageInstant()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var sourceTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral", SourceProvider, SourceModel);
        await SeedAgentAsync(targetId, "Financeiro", TargetProvider, TargetModel);
        await SeedAgentDelegationAsync(sourceId, targetId);

        var messageInstant = new DateTimeOffset(2026, 3, 10, 9, 58, 0, TimeSpan.FromHours(-3));
        await SeedTaskAsync(
            sourceTaskId, sourceId, contextId, "Preciso de um cálculo financeiro complexo.",
            messageMetadata: BuildMessageInstantMetadata(messageInstant));

        var toolName = ExpectedToolName("Financeiro");
        var sourceChatClient = BuildDelegationToolCallingChatClientMock(toolName, "Quanto é 2 + 2, considerando juros?");

        ChatOptions? capturedTargetOptions = null;
        var targetChatClient = new Mock<IChatClient>();
        targetChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, options, _) => capturedTargetOptions = options)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "O resultado é 4.")));

        var clients = ClientsByProviderModel(sourceChatClient.Object, targetChatClient.Object);

        // T1 e T2 — instantes de processamento diferentes por instância
        // (design.md, teste da Tarefa 3.8). Qual instância acaba processando
        // Source e qual processa Target é não-determinístico (mesma fila,
        // sem afinidade — ver comentário da classe); o que garante o gap
        // real é que a instância que processa o Source fica ocupada
        // aguardando o Target, então necessariamente é a OUTRA instância
        // que consome a task delegada — T1 e T2 acabam associados a
        // Source/Target nessa ordem ou na inversa, mas sempre diferentes
        // entre si.
        var timeProviderA = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero));
        var timeProviderB = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 12, 30, 0, TimeSpan.Zero));

        using var instanceA = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20), timeProvider: timeProviderA);
        using var instanceB = BuildHost(clients, delegationTimeout: TimeSpan.FromSeconds(20), timeProvider: timeProviderB);

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PublishJobAsync(sourceTaskId, sourceId, contextId);
            var sourceRecord = await PollUntilTerminalAsync(sourceTaskId);
            Assert.Equal(nameof(TaskState.Completed), sourceRecord.State);
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        var targetRecord = await GetLatestTaskRecordForAgentAsync(targetId);
        Assert.NotNull(targetRecord);
        Assert.Equal(nameof(TaskState.Completed), targetRecord!.State);

        Assert.NotNull(capturedTargetOptions);
        var targetInstructions = capturedTargetOptions!.Instructions!;

        // O mesmo messageInstant chegou ao Target, mesmo processado num
        // instante de relógio diferente do Source.
        Assert.Contains("Instante da mensagem", targetInstructions);
        Assert.Contains("2026-03-10T09:58:00-03:00", targetInstructions);

        // Prova de que os processing instants de fato divergiram (gap
        // real, não coincidência) — um dos dois instantes de processamento
        // aparece no bloco do Target, e é diferente do instante da
        // mensagem.
        var targetHasT1 = targetInstructions.Contains("2026-03-10T10:00:00+00:00");
        var targetHasT2 = targetInstructions.Contains("2026-03-10T12:30:00+00:00");
        Assert.True(targetHasT1 || targetHasT2, $"Instructions do Target não contêm nenhum dos dois instantes de processamento esperados: {targetInstructions}");
    }

    private static Dictionary<string, JsonElement> BuildMessageInstantMetadata(DateTimeOffset messageInstant) =>
        new() { [MessageInstantCodec.MetadataKey] = MessageInstantCodec.Encode(messageInstant) };

    private static DateTimeOffset? ExtractMessageInstantFromHistory(A2ATaskRecord record)
    {
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        if (lastUserMessage?.Metadata is null || !lastUserMessage.Metadata.TryGetValue(MessageInstantCodec.MetadataKey, out var value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value.GetString()!);
    }

    private static string ExpectedToolName(string targetAgentName) =>
        ToolNameSanitizer.Sanitize($"delegate_to_{ToolNameSlugifier.Slugify(targetAgentName)}");

    private static string? ExtractArtifactText(A2ATaskRecord record)
    {
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        return task.Artifacts?.LastOrDefault()?.Parts.FirstOrDefault(part => part.Text is not null)?.Text;
    }

    private static Dictionary<(string Provider, string Model), IChatClient> ClientsByProviderModel(
        IChatClient sourceChatClient, IChatClient? targetChatClient)
    {
        var clients = new Dictionary<(string, string), IChatClient>
        {
            [(SourceProvider, SourceModel)] = sourceChatClient,
        };

        if (targetChatClient is not null)
        {
            clients[(TargetProvider, TargetModel)] = targetChatClient;
        }

        return clients;
    }

    /// <summary>
    /// Mock de <see cref="IChatClient"/> cuja primeira resposta pede a
    /// chamada da tool de delegação (<paramref name="toolName"/>) com
    /// <paramref name="delegatedMessage"/> como argumento; a segunda
    /// resposta incorpora o resultado da tool call (o texto devolvido pela
    /// delegação) na resposta final — mesmo padrão de
    /// <c>McpToolExecutionEndToEndTests.BuildToolCallingChatClientMock</c>.
    /// </summary>
    private static Mock<IChatClient> BuildDelegationToolCallingChatClientMock(
        string toolName, string delegatedMessage, Action? beforeFirstCall = null)
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
                    beforeFirstCall?.Invoke();

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
        TimeSpan? delegationTimeout = null,
        TimeProvider? timeProvider = null)
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
            if (delegationTimeout is not null)
            {
                options.Timeout = delegationTimeout.Value;
                options.PollInterval = TimeSpan.FromMilliseconds(200);
            }
        });

        // Resolve por (provider, model), exatamente como o ChatClientResolver
        // real — nunca "esta instância sempre processa este agente" (ver
        // comentário na classe).
        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string provider, string model) => chatClientsByProviderModel[(provider, model)]);
        builder.Services.AddSingleton(resolverMock.Object);
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, AgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<IKnowledgeToolSetResolver, NullKnowledgeToolSetResolver>();
        builder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(
        Guid agentId, string agentName, string? provider = SourceProvider, string? model = SourceModel, bool isActive = true)
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {"Instruções de teste."}, {isActive}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task DeactivateAgentAsync(Guid agentId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE agents SET "IsActive" = false WHERE "Id" = {agentId}""");
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

    private async Task SeedTaskAsync(
        string taskId, Guid agentId, string contextId, string userMessage, int? delegationDepth = null,
        Dictionary<string, JsonElement>? messageMetadata = null)
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
                    Metadata = messageMetadata,
                },
            ],
        };

        if (delegationDepth is not null)
        {
            agentTask.Metadata = new Dictionary<string, JsonElement>
            {
                [DelegationDepth.MetadataKey] = DelegationDepth.Encode(delegationDepth.Value),
            };
        }

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

    private async Task<A2ATaskRecord?> GetLatestTaskRecordForAgentAsync(Guid agentId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.A2ATasks.AsNoTracking()
            .Where(t => t.AgentId == agentId)
            .OrderByDescending(t => t.StatusTimestamp)
            .FirstOrDefaultAsync();
    }
}
