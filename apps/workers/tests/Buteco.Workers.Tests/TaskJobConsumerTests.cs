using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.ExecutionMetrics;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

[Collection(WorkerHostCollection.Name)]
public class TaskJobConsumerTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    [Fact]
    public async Task Consumer_ProcessesJob_TaskEndsCompletedWithArtifact()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "Qual é a capital da França?");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
            var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
            Assert.NotNull(task.Artifacts);
            Assert.Contains(task.Artifacts!, artifact => artifact.Parts.Any(part => part.Text == "Paris"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// GUARDA DE R1 (design.md da change fix-vazamento-httpclient-chat): com o
    /// <see cref="IChatClient"/> cacheado por <c>(provider, model)</c> e
    /// compartilhado entre execuções, um descarte passa a quebrar TODAS as
    /// mensagens seguintes daquele par — e o sintoma seria "funciona só a
    /// primeira vez depois do boot", que é caro de ler.
    ///
    /// <para>
    /// NÃO reprova contra o defeito que esta change corrige: passa antes e
    /// depois dela, porque hoje nada descarta o client (<c>ChatClientAgent</c>
    /// não é <c>IDisposable</c> e <c>AgentExecutionService</c> não usa
    /// <c>using</c> no agente). Existe para reprovar no dia em que alguém
    /// acrescentar esse <c>using</c> — <c>DelegatingChatClient.Dispose()</c>
    /// descarta o <c>InnerClient</c> em cascata.
    /// </para>
    ///
    /// <para>
    /// Usa <see cref="DisposalTrackingChatClient"/> e não um <c>Mock</c>: o
    /// <c>Dispose()</c> de um mock é inócuo, então o mock passaria verde com o
    /// descarte em cascata presente.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Consumer_ProcessesTwoTasksInSequence_SharedChatClientIsNeverDisposed()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", firstTaskId, contextId, "primeira");

        var chatClient = new DisposalTrackingChatClient("ok");

        using var host = BuildHost(chatClient);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(firstTaskId, agentId, contextId);
            var first = await PollUntilTerminalAsync(firstTaskId);
            Assert.Equal(nameof(TaskState.Completed), first.State);

            await SeedTaskAsync(secondTaskId, agentId, contextId, "segunda");
            await PublishJobAsync(secondTaskId, agentId, contextId);
            var second = await PollUntilTerminalAsync(secondTaskId);

            // A asserção que importa: a SEGUNDA chega a completed. Se o client
            // compartilhado tivesse sido descartado pela primeira execução, ela
            // terminaria failed por ObjectDisposedException.
            Assert.Equal(nameof(TaskState.Completed), second.State);

            // Asserção determinística pareada (convenção 15, quinta forma): a
            // primeira sozinha depende de a exceção virar `failed`, que é o
            // caminho de degradação graciosa e poderia mascarar outra causa.
            // Esta afirma o artefato que a correção produz — o client nunca
            // recebeu Dispose.
            Assert.Equal(0, chatClient.DisposeCount);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenChatClientFails_TaskEndsFailed()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM indisponível"));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Failed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenAgentProviderNotConfiguredInWorkerEnvironment_TaskEndsFailed()
    {
        // Simula divergência de configuração entre apps/api e apps/workers
        // (ver design.md, Decision 5, camada complementar): o agente foi
        // cadastrado com um provider que apps/api considerou configurado,
        // mas o ambiente de apps/workers não tem a chave desse provider.
        // Usa o ChatClientResolver real (não mockado) para provar que ele
        // lança e o catch (Exception) já existente em
        // AgentExecutionService.ExecuteAsync trata o caso, sem derrubar o worker.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi", provider: "anthropic", model: "claude-opus-5");

        using var host = BuildHostWithRealResolver();
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Failed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenTaskRowNotYetCommittedAtConsumeTime_RetriesThenProcessesSuccessfully()
    {
        // Reproduz a corrida real entre A2AServer publicando no RabbitMQ e o
        // SaveTaskAsync do evento "submitted" ainda não ter commitado quando
        // o worker consome a mensagem — descoberta via teste manual, ver
        // design.md da change apps-workers-historico-conversa, Decisão 9.
        // Aqui simulamos publicando o job ANTES da task existir no store, e
        // só inserindo a task (com atraso) depois.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            // Atraso maior que uma tentativa de retry (100ms cada), menor que
            // o total (5 tentativas) — garante que o worker precisou
            // realmente tentar de novo, não passou de primeira por sorte.
            await Task.Delay(250);
            await SeedTaskAsync(taskId, agentId, contextId, "Qual é a capital da França?");

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenTaskExistsButUserMessageNotYetPersisted_RetriesUntilMessageIsPresent()
    {
        // Segunda camada da mesma corrida (design.md, Decisão 9): o A2AServer
        // grava a task em DOIS eventos separados — SubmitAsync() cria a
        // linha (History nulo) e só depois, após uma leitura de estado do
        // agente, EnqueueMessageAsync grava a mensagem do usuário nela —
        // enquanto publica no RabbitMQ concorrentemente. Um worker pode
        // consumir a mensagem e achar a task já criada, mas ainda sem a
        // mensagem do usuário. Aqui simulamos publicando o job com a task já
        // existente porém sem History, adicionando a mensagem só depois de
        // um atraso.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskWithoutUserMessageAsync(taskId, agentId, contextId);

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            // Atraso maior que uma tentativa de retry (100ms cada), menor que
            // o total (5 tentativas) — garante que o worker precisou
            // realmente tentar de novo, não passou de primeira por sorte.
            await Task.Delay(250);
            await AddUserMessageToTaskAsync(taskId, contextId, "Qual é a capital da França?");

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Métricas de execução (change metricas-execucao-coleta) ───────────
    // Os hosts destes guardas compõem o LlmCallDurationChatClient real por
    // cima do client falso (BuildMeasuredHost), como o ChatClientResolver faz
    // em produção — sem ele não há quem registre a linha filha.

    private string ConnectionString => fixture.Postgres.GetConnectionString();

    [Fact]
    public async Task CompletedTask_ProducesClosedExecutionRow_AndOneTurnCall()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        var submittedAt = await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "Qual é a capital da França?");

        using var host = BuildMeasuredHost(ReplyingClient(new UsageDetails { InputTokenCount = 40, OutputTokenCount = 2 }));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);
            Assert.Equal(nameof(TaskState.Completed), (await PollUntilTerminalAsync(taskId)).State);

            var execution = await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, taskId);

            Assert.Equal(agentId, execution.AgentId);
            Assert.Equal(ExecutionMetricsValues.Origin.External, execution.Origin);
            Assert.Null(execution.SourceAgentId);
            Assert.Equal(nameof(TaskState.Completed), execution.TerminalState);
            Assert.Null(execution.FailurePhase);
            Assert.Equal("openai", execution.Provider);
            Assert.Equal("gpt-5.6-sol", execution.Model);
            Assert.NotNull(execution.SubmittedAt);
            Assert.True(Math.Abs((execution.SubmittedAt!.Value - submittedAt).TotalMilliseconds) < 1);
            Assert.True(execution.SubmittedAt <= execution.StartedAt);
            Assert.True(execution.StartedAt <= execution.LockAcquiredAt);
            Assert.True(execution.LockAcquiredAt <= execution.EndedAt);

            var call = Assert.Single(await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, taskId));
            Assert.Equal(ExecutionMetricsValues.Purpose.Turn, call.Purpose);
            Assert.Equal(40, call.InputTokens);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// A linha nasce ANTES de a task terminar (D3) — é o que torna visível a
    /// execução que nunca chega ao fim. Ancorado em estado persistido: a linha
    /// é lida enquanto a chamada ao provedor está segura num
    /// <see cref="TaskCompletionSource"/>, nunca por relógio.
    /// </summary>
    [Fact]
    public async Task ExecutionInProgress_RowIsAlreadyOpen_BeforeTheTaskEnds()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            });

        using var host = BuildMeasuredHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var open = await ExecutionMetricsReader.WaitForExecutionAsync(ConnectionString, taskId);
            Assert.Null(open.EndedAt);
            Assert.Null(open.TerminalState);

            release.TrySetResult();
            var closed = await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, taskId);
            Assert.Equal(nameof(TaskState.Completed), closed.TerminalState);
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync();
        }
    }

    /// <summary>
    /// D4: numa reentrega a task lida já está em <c>Working</c>, e o carimbo que
    /// ela carrega é o do início da tentativa anterior. Gravá-lo como instante do
    /// <c>submitted</c> afirmaria um tempo de fila que não foi medido.
    /// </summary>
    [Fact]
    public async Task RedeliveredTask_AlreadyWorking_HasNullSubmittedAt_NotTheWorkingTimestamp()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskAsync(taskId, agentId, contextId, "oi", TaskState.Working);

        using var host = BuildMeasuredHost(ReplyingClient(usage: null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var execution = await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, taskId);
            Assert.Null(execution.SubmittedAt);
            Assert.Equal(nameof(TaskState.Completed), execution.TerminalState);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Convenção 13 no banco, não só em memória: o ponto em que um nulo vira
    /// zero pode ser o mapeamento, a coluna ou o caminho de escrita — por isso o
    /// guarda lê a linha persistida. Negativo de propósito: afirma a AUSÊNCIA
    /// do zero, que é a normalização que ele existe para impedir.
    /// </summary>
    [Fact]
    public async Task UnreportedCacheTokens_ArePersistedAsNull_NotZero()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi");

        using var host = BuildMeasuredHost(ReplyingClient(new UsageDetails { InputTokenCount = 12, OutputTokenCount = 3, CachedInputTokenCount = null }));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, taskId);

            var call = Assert.Single(await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, taskId));
            Assert.Equal(12, call.InputTokens);
            Assert.Null(call.CachedInputTokens);
            Assert.NotEqual(0, call.CachedInputTokens);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// D13, o guarda comportamental: o consumo de ontem pertence ao modelo de
    /// ontem. O par estrutural (nenhuma FK para <c>agents</c> no modelo do EF)
    /// está em <c>ExecutionMetricsSchemaMirrorTests</c>.
    /// </summary>
    [Fact]
    public async Task AgentSwitchingModel_DoesNotRewriteTheModelOfEarlierRows()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", firstTaskId, contextId, "primeira", model: "modelo-a");

        using var host = BuildMeasuredHost(ReplyingClient(usage: null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(firstTaskId, agentId, contextId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, firstTaskId);

            await SetAgentModelAsync(agentId, "modelo-b");
            await SeedTaskAsync(secondTaskId, agentId, contextId, "segunda");
            await PublishJobAsync(secondTaskId, agentId, contextId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, secondTaskId);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal("modelo-a", (await ExecutionMetricsReader.FindExecutionAsync(ConnectionString, firstTaskId))!.Model);
        Assert.Equal("modelo-a", Assert.Single(await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, firstTaskId)).Model);
        Assert.Equal("modelo-b", (await ExecutionMetricsReader.FindExecutionAsync(ConnectionString, secondTaskId))!.Model);
        Assert.Equal("modelo-b", Assert.Single(await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, secondTaskId)).Model);
    }

    /// <summary>
    /// A falha de telemetria nunca muda o estado terminal (D3, convenção 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por que renomear a tabela, e não mockar o escritor.</b> Não há ponto de
    /// injeção: o escritor não está em DI de propósito (D2 — injetar custaria os
    /// 14 harness que registram <c>AgentExecutionService</c>). E renomear
    /// exercita a falha real que o deploy pode produzir: um worker novo contra
    /// um banco em que a migração ainda não rodou.
    /// </para>
    ///
    /// <para>
    /// <b>Seguro dentro da coleção:</b> as classes de uma coleção do xUnit rodam
    /// em série, e o container desta classe é dela (fixture por classe). O
    /// <c>finally</c> restaura antes de qualquer outro teste.
    /// </para>
    ///
    /// <para>
    /// <b>Por que o log entra na asserção:</b> só o estado <c>Completed</c> passa
    /// também sem coleta nenhuma — é o log que prova que a gravação foi tentada,
    /// falhou, e mesmo assim a task terminou certa.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task MetricsTableMissing_TaskStillCompletesWithArtifact_AndFailureIsLogged()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi");

        var logs = new List<(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State)>();
        await ExecuteSqlAsync("ALTER TABLE task_executions RENAME TO task_executions_indisponivel;");

        try
        {
            using var host = BuildMeasuredHost(ReplyingClient(usage: null), logs);
            await host.StartAsync();

            try
            {
                await PublishJobAsync(taskId, agentId, contextId);
                var record = await PollUntilTerminalAsync(taskId);

                Assert.Equal(nameof(TaskState.Completed), record.State);
                var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
                Assert.Contains(task.Artifacts!, artifact => artifact.Parts.Any(part => part.Text == "ok"));

                await PollUntilAsync(() => MetricsWriteFailureLogged(logs, taskId));
            }
            finally
            {
                await host.StopAsync();
            }
        }
        finally
        {
            await ExecuteSqlAsync("ALTER TABLE task_executions_indisponivel RENAME TO task_executions;");
        }
    }

    [Fact]
    public async Task ProviderNotConfigured_ProducesFailedRow_WithClientResolutionPhase_AndNoCalls()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi", provider: "anthropic", model: "claude-opus-5");

        using var host = BuildHostWithRealResolver();
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var execution = await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, taskId);
            Assert.Equal(nameof(TaskState.Failed), execution.TerminalState);
            Assert.Equal(ExecutionMetricsValues.FailurePhase.ChatClientResolution, execution.FailurePhase);
            Assert.Empty(await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, taskId));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Guarda de estado terminal (change metricas-execucao-coleta, escopo 2) ─
    // Defeito pré-existente: uma mensagem que o RabbitMQ entrega DE NOVO (worker
    // parou entre gravar o estado terminal e confirmar a mensagem) reexecutava a
    // task a partir de `Completed` — o LLM era chamado outra vez e o estado era
    // reescrito. A coleta de métricas alargou a janela entre gravar e confirmar,
    // e o defeito passou a aparecer na suíte (medido: toda falha instável das
    // classes de delegação coincidiu com uma reentrega de task terminal —
    // design.md, D17).

    public static TheoryData<TaskState> TerminalStates =>
        [TaskState.Completed, TaskState.Failed, TaskState.Rejected, TaskState.Canceled];

    [Theory]
    [MemberData(nameof(TerminalStates))]
    public async Task RedeliveredTerminalTask_IsNotExecutedAgain_AndIsAcknowledged(TaskState terminalState)
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskAsync(taskId, agentId, contextId, "oi", terminalState);

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "reexecutou")));

        // Sentinela: uma task normal publicada DEPOIS da terminal, no mesmo host.
        // Com prefetchCount: 1 e um consumidor só, as mensagens são processadas em
        // ordem — quando a sentinela termina, a terminal já foi processada E
        // confirmada. Âncora em estado persistido, nunca em relógio nem em "fila
        // vazia": a mensagem em voo some da contagem de prontas assim que é
        // entregue, e um guarda ancorado nela olharia antes da reexecução e
        // passaria verde com o defeito presente.
        var sentinelTaskId = Guid.NewGuid().ToString("N");
        var sentinelContextId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(sentinelTaskId, agentId, sentinelContextId, "sentinela");

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);
            await PublishJobAsync(sentinelTaskId, agentId, sentinelContextId);
            Assert.Equal(nameof(TaskState.Completed), (await PollUntilTerminalAsync(sentinelTaskId)).State);
        }
        finally
        {
            await host.StopAsync();
        }

        // Uma chamada só: a da sentinela. Reexecutar a terminal faria duas.
        chatClient.Verify(
            client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(ConnectionString).Options;
        await using var dbContext = new AppDbContext(options);
        var record = await dbContext.A2ATasks.AsNoTracking().SingleAsync(t => t.TaskId == taskId);
        Assert.Equal(terminalState.ToString(), record.State);
        Assert.Null(await ExecutionMetricsReader.FindExecutionAsync(ConnectionString, taskId));
    }

    /// <summary>
    /// O PAR, e é o guarda mais importante desta guarda: se ela errar para o
    /// lado largo, a conversa para de responder EM SILÊNCIO — pior que o defeito
    /// que ela corrige. A mensagem seguinte de uma conversa cuja task anterior
    /// terminou chega como task NOVA, no mesmo <c>contextId</c>, em
    /// <c>Submitted</c>: o <c>A2AServer</c> recusa mensagem para task terminal
    /// (<c>GuardTerminalState</c>, decompilado do A2A 1.0.0-preview2; a premissa
    /// tem guarda próprio em <c>apps/api</c>, <c>A2ATaskLifecycleTests</c>), e o
    /// inbox manda só o <c>contextId</c>. Passa antes e depois da guarda.
    /// </summary>
    [Fact]
    public async Task NextMessageAfterACompletedTask_IsANewTask_AndExecutesNormally()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", firstTaskId, contextId, "primeira");

        using var host = BuildHost(ReplyingClient(usage: null));
        await host.StartAsync();

        try
        {
            await PublishJobAsync(firstTaskId, agentId, contextId);
            Assert.Equal(nameof(TaskState.Completed), (await PollUntilTerminalAsync(firstTaskId)).State);

            await SeedTaskAsync(secondTaskId, agentId, contextId, "segunda");
            await PublishJobAsync(secondTaskId, agentId, contextId);

            var second = await PollUntilTerminalAsync(secondTaskId);
            Assert.Equal(nameof(TaskState.Completed), second.State);
            var task = JsonSerializer.Deserialize<AgentTask>(second.Payload, A2AJsonUtilities.DefaultOptions)!;
            Assert.Contains(task.Artifacts!, artifact => artifact.Parts.Any(part => part.Text == "ok"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static IChatClient ReplyingClient(UsageDetails? usage)
    {
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage });
        return chatClient.Object;
    }

    private static bool MetricsWriteFailureLogged(
        List<(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State)> logs, string taskId)
    {
        lock (logs)
        {
            return logs.Any(entry =>
                entry.Level == LogLevel.Warning
                && entry.State.Any(pair => pair.Key == "MetricsWriteStage")
                && entry.State.Any(pair => pair.Key == "TaskId" && pair.Value as string == taskId));
        }
    }

    private static async Task PollUntilAsync(Func<bool> condition, int maxAttempts = 75)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("A condição esperada não aconteceu a tempo.");
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetAgentModelAsync(Guid agentId, string model)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(ConnectionString).Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""UPDATE agents SET "Model" = {model} WHERE "Id" = {agentId}""");
    }

    /// <summary>
    /// Como <see cref="BuildHost"/>, mas o resolver compõe o
    /// <see cref="LlmCallDurationChatClient"/> real por cima do client falso,
    /// com o <c>(provider, model)</c> pedido — o que o
    /// <see cref="ChatClientResolver"/> faz em produção.
    /// </summary>
    private IHost BuildMeasuredHost(
        IChatClient chatClient, List<(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State)>? logs = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = ConnectionString;
        builder.Services.AddInfrastructure(builder.Configuration);

        if (logs is not null)
        {
            builder.Logging.AddProvider(new CapturingLoggerProvider(logs));
        }

        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = fixture.RabbitMq.Hostname;
            options.Port = fixture.RabbitMq.GetMappedPublicPort(5672);
            options.Username = "buteco";
            options.Password = "buteco_test_password";
        });

        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string provider, string model) =>
                new LlmCallDurationChatClient(chatClient, provider, model, NullLogger<LlmCallDurationChatClient>.Instance));
        builder.Services.AddSingleton(resolverMock.Object);
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
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

    private sealed class CapturingLoggerProvider(
        List<(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            List<(LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                IReadOnlyList<KeyValuePair<string, object?>> pairs = state is IReadOnlyList<KeyValuePair<string, object?>> structured
                    ? structured.ToList()
                    : [];

                lock (entries)
                {
                    entries.Add((logLevel, pairs));
                }
            }
        }
    }

    private IHost BuildHostWithRealResolver()
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

        // AnthropicOptions/GeminiOptions nunca configurados aqui de propósito —
        // é exatamente o ambiente "chave ausente" que este teste exercita.
        builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
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

    private IHost BuildHost(IChatClient chatClient)
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
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task<DateTimeOffset> SeedAgentAndTaskAsync(
        Guid agentId,
        string agentName,
        string instructions,
        string taskId,
        string contextId,
        string userMessage,
        string provider = "openai",
        string model = "gpt-5.6-sol")
    {
        await SeedAgentAsync(agentId, agentName, instructions, provider, model);
        return await SeedTaskAsync(taskId, agentId, contextId, userMessage);
    }

    private async Task SeedAgentAsync(
        Guid agentId, string agentName, string instructions, string provider = "openai", string model = "gpt-5.6-sol")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task<DateTimeOffset> SeedTaskAsync(
        string taskId, Guid agentId, string contextId, string userMessage, TaskState state = TaskState.Submitted)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = state, Timestamp = now },
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
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, state.ToString(), now, payload));
        await dbContext.SaveChangesAsync();
        return now;
    }

    private async Task SeedTaskWithoutUserMessageAsync(string taskId, Guid agentId, string contextId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = now },
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, nameof(TaskState.Submitted), now, payload));
        await dbContext.SaveChangesAsync();
    }

    private async Task AddUserMessageToTaskAsync(string taskId, string contextId, string userMessage)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var record = await dbContext.A2ATasks.FirstAsync(t => t.TaskId == taskId);
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        task.History =
        [
            new Message
            {
                Role = Role.User,
                Parts = [Part.FromText(userMessage)],
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
            },
        ];

        var payload = JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);
        record.Update(task.ContextId, task.Status.State.ToString(), task.Status.Timestamp, payload);
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
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;

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
}
