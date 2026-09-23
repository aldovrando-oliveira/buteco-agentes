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
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-resumo-historico-conversa: resumo incremental
/// do histórico de conversa via <c>CompactionProvider</c>/
/// <c>SummarizationCompactionStrategy</c> (ver design.md e
/// specs/a2a-task-lifecycle/spec.md daquela change).
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class HistorySummarizationTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    // Devem bater com as constantes privadas de AgentExecutionService (não
    // vale a pena expor via InternalsVisibleTo só para os testes). Se os
    // valores lá mudarem, estes testes precisam acompanhar.
    private const int SummarizationTurnThreshold = 10;
    private const int MaxHistoryMessages = 200;

    [Fact]
    public async Task BelowThreshold_NoSummarizationCallHappens()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold - 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        List<ChatMessage>? lastMainCallMessages = null;
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out var mainCallCapture);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        lastMainCallMessages = mainCallCapture();

        // Comportamento idêntico à change anterior, sem regressão: nenhuma
        // chamada de resumo, histórico cru completo chega ao LLM.
        Assert.Empty(summaryCallInputs);
        Assert.NotNull(lastMainCallMessages);
        Assert.Contains(lastMainCallMessages!, m => m.Text == "pergunta 1");
        Assert.Contains(lastMainCallMessages!, m => m.Text == $"pergunta {turns}");
    }

    [Fact]
    public async Task CrossingThreshold_TriggersSummarization_AndMainCallReceivesSummaryPlusRecentTurns()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold + 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out var mainCallCapture);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        var lastMainCallMessages = mainCallCapture();

        // (a) uma chamada de resumo foi feita ao cruzar o limiar.
        Assert.NotEmpty(summaryCallInputs);

        // (b) a chamada de conversa real recebe o resumo + turnos recentes
        // preservados no lugar do histórico cru completo da porção mais
        // antiga: a primeira pergunta (bem além do que MinimumPreservedGroups
        // preserva cru) já foi resumida e não chega mais crua ao LLM; a
        // pergunta mais recente continua presente.
        Assert.NotNull(lastMainCallMessages);
        Assert.Contains(lastMainCallMessages!, m => m.Text != null && m.Text.StartsWith("[Summary]"));
        Assert.DoesNotContain(lastMainCallMessages!, m => m.Text == "pergunta 1");
        Assert.Contains(lastMainCallMessages!, m => m.Text == $"pergunta {turns}");
    }

    /// <summary>
    /// A chamada de resumo é uma requisição HTTP real, paga, e não é um turno do
    /// usuário (Q3 da exploração <c>metricas-de-operacao</c>). Sem a marca de
    /// finalidade, ela entraria nas métricas como turno e inflaria "chamadas por
    /// task". Change <c>metricas-execucao-coleta</c>, D8.
    /// </summary>
    [Fact]
    public async Task CrossingThreshold_RecordsCompactionCall_SeparateFromTurnCalls_InTheSameTask()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId);

        var chatClient = BuildSummarizingChatClientMock([], out _);

        // Composto com o LlmCallDurationChatClient real, como o
        // ChatClientResolver faz em produção — sem ele ninguém registra as
        // linhas filhas.
        using var host = BuildHost(new LlmCallDurationChatClient(
            chatClient.Object, "openai", "gpt-5.6-sol", NullLogger<LlmCallDurationChatClient>.Instance));
        await host.StartAsync();

        var taskIds = new List<string>();
        try
        {
            for (var turn = 1; turn <= SummarizationTurnThreshold + 1; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                taskIds.Add(record.TaskId);
                await ExecutionMetricsReader.WaitForClosedExecutionAsync(fixture.Postgres.GetConnectionString(), record.TaskId);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        var callsByTask = new List<IReadOnlyList<Buteco.Workers.ExecutionMetrics.Entities.ProviderCall>>();
        foreach (var taskId in taskIds)
        {
            callsByTask.Add(await ExecutionMetricsReader.ProviderCallsAsync(fixture.Postgres.GetConnectionString(), taskId));
        }

        // Quantas vezes o gatilho dispara em N turnos é do pacote, não desta
        // change — o guarda afirma só que a compactação aparece separada, e que
        // ela convive com o turno DENTRO da mesma task (é inline no RunAsync).
        var compactingTasks = callsByTask.Where(calls => calls.Any(call => call.Purpose == ExecutionMetricsValues.Purpose.Compaction)).ToList();
        Assert.NotEmpty(compactingTasks);
        Assert.All(compactingTasks, calls => Assert.Contains(calls, call => call.Purpose == ExecutionMetricsValues.Purpose.Turn));
        Assert.All(callsByTask[0], call => Assert.Equal(ExecutionMetricsValues.Purpose.Turn, call.Purpose));
    }

    [Fact]
    public async Task IncrementalSummarization_AcrossManyTriggers_PreviousSummaryFeedsIntoNextSummarizationCall()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        // Margem generosa para garantir vários cruzamentos do limiar
        // (MinimumPreservedGroups padrão do pacote é 8 grupos ~ 4 turnos,
        // então o gatilho volta a disparar a cada poucos turnos depois do
        // primeiro resumo) — este é o teste que teria pego a regressão
        // achada via /opsx:explore antes do apply (design.md, Decisão 9):
        // CompactionMessageIndex.Update descarta todo o bookkeeping
        // acumulado sempre que RecentMessageChatReducer trunca a lista de
        // mensagens que o alimenta, então um teste de só um ou dois
        // gatilhos não é suficiente para provar estabilidade.
        var turns = 40;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out _);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        // Múltiplos gatilhos de resumo ao longo da mesma conversa — prova
        // de que o mecanismo não morre depois da primeira vez.
        Assert.True(summaryCallInputs.Count >= 3, $"Esperava pelo menos 3 chamadas de resumo, houve {summaryCallInputs.Count}.");

        // O segundo gatilho de resumo recebe o resumo anterior entre as
        // mensagens enviadas para o LLM — prova de que o estado do
        // CompactionProvider não foi resetado silenciosamente entre o
        // primeiro e o segundo gatilho (o bug que a investigação achou).
        var secondSummaryCallInput = summaryCallInputs[1];
        Assert.Contains(secondSummaryCallInput, m => m.Text != null && m.Text.StartsWith("[Summary]"));

        // A primeira pergunta, já resumida no primeiro gatilho, nunca mais
        // reaparece como mensagem crua em nenhuma chamada de resumo
        // seguinte — prova de que não é "resumir do zero a cada gatilho".
        foreach (var laterCallInput in summaryCallInputs.Skip(1))
        {
            Assert.DoesNotContain(laterCallInput, m => m.Text == "pergunta 1");
        }
    }

    [Fact]
    public async Task SummarizationCallFailure_DoesNotPreventUserTurnFromCompleting()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold + 2;

        await SeedAgentAsync(agentId);

        var chatClient = new Mock<IChatClient>();

        // Chamada de resumo (ChatOptions null, ver
        // SummarizationCompactionStrategy.CompactCoreAsync decompilado)
        // sempre falha, simulando o provider do agente indisponível.
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o == null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider de resumo indisponível"));

        // Chamada de conversa real (ChatOptions não-null) continua
        // funcionando normalmente.
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");

                // (a) e (b): mesmo cruzando o limiar (turno
                // SummarizationTurnThreshold + 1 em diante, onde a chamada
                // de resumo falha), a task do turno corrente ainda assim
                // chega a completed — a falha na chamada de resumo não é
                // tratada como falha da task inteira.
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task CompactionState_SurvivesRealSerializeDeserializeRoundTrip_AcrossSeparateWorkerInstances()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turnsBeforeRestart = SummarizationTurnThreshold + 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputsA = new List<List<ChatMessage>>();
        var chatClientA = BuildSummarizingChatClientMock(summaryCallInputsA, out _);

        // Instância A: processa turnos suficientes para cruzar o limiar de
        // resumo uma vez, depois é totalmente descartada (host parado e
        // disposed) — nenhum objeto compartilhado com a instância B além do
        // Postgres do fixture (mesmo padrão de prova de "sem estado em
        // memória" já usado em ConversationHistoryTests).
        using (var hostA = BuildHost(chatClientA.Object))
        {
            await hostA.StartAsync();

            for (var turn = 1; turn <= turnsBeforeRestart; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }

            await hostA.StopAsync();
        }

        Assert.NotEmpty(summaryCallInputsA);

        // Instância B: construída do zero, nunca teve acesso a nenhum
        // objeto/mock da instância A — só pode ver o resumo gerado por A se
        // ele realmente sobreviveu ao ciclo SerializeSessionAsync (fim de
        // A) -> Postgres -> DeserializeSessionAsync (início de B).
        var summaryCallInputsB = new List<List<ChatMessage>>();
        var chatClientB = BuildSummarizingChatClientMock(summaryCallInputsB, out var mainCallCaptureB);

        using (var hostB = BuildHost(chatClientB.Object))
        {
            await hostB.StartAsync();
            var record = await RunTurnAsync(agentId, contextId, "pergunta depois do restart");
            Assert.Equal(nameof(TaskState.Completed), record.State);
            await hostB.StopAsync();
        }

        var firstMainCallMessagesInB = mainCallCaptureB();

        // A primeiríssima chamada de conversa real da instância B já
        // recebe o resumo produzido pela instância A, e não a pergunta 1
        // crua — prova de que o estado incremental do CompactionProvider
        // (não só o histórico bruto) sobreviveu ao round-trip real de
        // serialização entre processos distintos.
        Assert.NotNull(firstMainCallMessagesInB);
        Assert.Contains(firstMainCallMessagesInB!, m => m.Text != null && m.Text.StartsWith("[Summary]"));
        Assert.DoesNotContain(firstMainCallMessagesInB!, m => m.Text == "pergunta 1");
    }

    [Fact]
    public async Task ConversationExceedingMaxHistoryMessages_DegradesGracefully_WithoutFailingTasks()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        // Margem além de MaxHistoryMessages (mesmo estilo de margem já
        // usado em ConversationHistoryTests.HistoryExceedingLimit_...):
        // além do teto de segurança da Decisão 9 do design.md, o
        // RecentMessageChatReducer volta a truncar ativamente, o que
        // reabre a colisão com o bookkeeping do CompactionProvider
        // (Groups.Clear() a cada turno) — o objetivo aqui não é provar que
        // o resumo continua funcionando nessa faixa (Risks/Trade-offs do
        // design.md documenta que não continua), e sim que o worker não
        // lança exceção nem falha a task por causa disso: degrada de volta
        // para o comportamento de truncamento puro pré-existente, sem
        // crash.
        var turns = (MaxHistoryMessages / 2) + 3;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out _);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");

                // Nenhum crash / task failed por causa da colisão entre
                // truncamento e compactação além do teto de segurança —
                // comportamento aceito como o limite conhecido (Risks do
                // design.md), não como bug.
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// <b>Escopo 2 da change <c>compactacao-historico</c>:</b> a falha da chamada
    /// de resumo passa a aparecer em log.
    ///
    /// <para>
    /// <b>Era invisível por construção, não por nível de log.</b> A
    /// <c>SummarizationCompactionStrategy</c> captura a exceção, desfaz a
    /// exclusão dos grupos e registra um aviso — num logger que vinha do
    /// <c>NullLoggerFactory</c>, porque o <c>CompactionProvider</c> era
    /// construído sem <c>loggerFactory</c>. O diagnóstico do defeito do Gemini
    /// custou duas rodadas de exploração, um harness e uma chave de dev
    /// justamente por causa deste silêncio.
    /// </para>
    ///
    /// <para>
    /// O guarda afirma a linha, não o texto dela: a mensagem é do pacote, e
    /// prendê-la inteira seria prender versão de dependência.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FalhaNaChamadaDeResumo_ApareceEmLogDeAviso()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold + 2;

        await SeedAgentAsync(agentId);

        var chatClient = new Mock<IChatClient>();

        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o == null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Requests ending with a model turn are not supported."));

        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var logs = new List<(LogLevel Level, string Message)>();
        using var host = BuildHost(chatClient.Object, mcpToolSetResolver: null, capturedLogs: logs);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");

                // O turno do usuário continua chegando a completed: a falha de
                // resumo aparece, mas não derruba a conversa.
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Contains(
            logs,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("Summarization failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Escopo 1 da change <c>compactacao-historico</c>, fim a fim:</b> com a
    /// chamada de resumo funcionando contra um provedor que recusa requisição
    /// terminada em turno de modelo, e com <b>tool chamada em todo turno</b>, a
    /// entrada do turno para de crescer.
    ///
    /// <para>
    /// <b>Por que com tool.</b> A sessão 1 do piloto tinha delegações, e grupos
    /// <c>ToolCall</c> são atômicos no índice de compactação — a dúvida era se a
    /// estratégia conseguiria excluí-los. Medido em 40 turnos na exploração de
    /// 22/09/2026: consegue, e o patamar se segura (entrada oscilando entre ~190
    /// e ~3.570, teto parado entre o 21º e o 40º turno, contra crescimento
    /// monótono até 4.082 com a compactação falhando).
    /// </para>
    ///
    /// <para>
    /// <b>A MEDIDA CONTA TODO O CONTEÚDO, NÃO SÓ <c>ChatMessage.Text</c></b> —
    /// ver <see cref="TamanhoDoConteudo"/>. Somar só texto deixa
    /// <c>FunctionCallContent</c> e <c>FunctionResultContent</c> de fora, e foi
    /// o erro de instrumento que quase fez a exploração reportar patamar falso:
    /// os dois cenários (compactação funcionando e compactação falhando) davam
    /// <b>185–189 tokens idênticos</b>. Um teste que meça só texto não distingue
    /// os dois comportamentos.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CompactacaoComToolNoHistorico_SeguraOPatamarDeEntrada()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        // Margem além do limiar: o patamar só é afirmável depois de vários
        // gatilhos seguidos, não do primeiro.
        var turns = SummarizationTurnThreshold + 9;

        await SeedAgentAsync(agentId);

        var chatClient = new ToolCallingChatClient();
        using var host = BuildHost(chatClient, new SingleToolSetResolver());
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        // (a) a compactação aconteceu de verdade — sem isto, "não cresceu"
        // poderia ser um histórico que nunca chegou ao limiar.
        Assert.NotEmpty(chatClient.ResumosProduzidos);

        // (b) e nenhuma tentativa foi recusada pelo provedor: é o defeito que
        // esta change corrige, e ele é silencioso por construção.
        Assert.Empty(chatClient.ResumosRecusados);

        // (c) o patamar. A referência é o turno em que o primeiro resumo entrou
        // no histórico; daí em diante a entrada oscila, mas não sobe.
        var entradas = chatClient.EntradasPorTurno;
        Assert.Equal(turns, entradas.Count);

        var referencia = entradas[SummarizationTurnThreshold];
        var picoDepois = entradas.Skip(SummarizationTurnThreshold + 1).Max();

        Assert.True(
            picoDepois <= referencia * 1.2,
            $"A entrada deveria ter parado de crescer depois do primeiro resumo: referência {referencia}, "
          + $"pico posterior {picoDepois}. Série: {string.Join(", ", entradas)}");
    }

    /// <summary>
    /// Soma o conteúdo de todas as mensagens — texto, chamada de tool e
    /// resultado de tool. Ver o comentário do teste acima para por que
    /// <c>ChatMessage.Text</c> sozinho não serve.
    /// </summary>
    private static int TamanhoDoConteudo(IEnumerable<ChatMessage> messages) =>
        messages.Sum(m => m.Contents.Sum(content => content switch
        {
            TextContent t => (t.Text ?? string.Empty).Length,
            FunctionCallContent c => c.Name.Length + JsonSerializer.Serialize(c.Arguments).Length,
            FunctionResultContent r => (r.Result?.ToString() ?? string.Empty).Length,
            _ => content.ToString()?.Length ?? 0,
        }));

    /// <summary>
    /// Resolve UMA tool para o agente, com resultado grande — da ordem de uma
    /// resposta de delegação, que é o que o histórico do piloto carregava.
    /// </summary>
    private sealed class SingleToolSetResolver : IMcpToolSetResolver
    {
        public Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolSet(
                [
                    AIFunctionFactory.Create(
                        (string conta) => string.Join(
                            " ",
                            Enumerable.Range(0, 400).Select(i => $"linha{i} do extrato da conta {conta};")),
                        "consultar_saldo"),
                ],
                []));
    }

    /// <summary>
    /// Provedor simulado que (a) pede a tool em todo turno e (b) <b>recusa
    /// requisição de resumo terminada em turno de modelo</b>, como o Gemini faz
    /// — <c>400</c>, <i>"Requests ending with a model turn are not
    /// supported."</i>, reproduzido em 22/09/2026 contra
    /// <c>gemini-3.6-flash</c>. Sem essa recusa, o teste ficaria verde mesmo com
    /// o defeito de pé.
    ///
    /// <para>
    /// Não é <c>Mock&lt;IChatClient&gt;</c> porque a resposta depende do estado
    /// da conversa (pedir tool × responder texto), e não só dos argumentos.
    /// </para>
    /// </summary>
    /// <summary>
    /// Captura nível e mensagem de cada linha de log do host — o mesmo idioma
    /// de <c>TimeZoneStartupValidationTests</c>.
    /// </summary>
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
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }

    private sealed class ToolCallingChatClient : IChatClient
    {
        private readonly Lock _gate = new();

        public List<int> EntradasPorTurno { get; } = [];

        public List<string> ResumosProduzidos { get; } = [];

        public List<string> ResumosRecusados { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = messages.ToList();

            // ChatOptions nulo é a chamada de resumo — a estratégia do pacote
            // sempre chama assim (decompilado, CompactCoreAsync).
            if (options is null)
            {
                if (list.Count > 0 && list[^1].Role == ChatRole.Assistant)
                {
                    lock (_gate)
                    {
                        ResumosRecusados.Add(list[^1].Text ?? string.Empty);
                    }

                    throw new HttpRequestException("Requests ending with a model turn are not supported.");
                }

                lock (_gate)
                {
                    ResumosProduzidos.Add(list[^1].Text ?? string.Empty);
                }

                return Task.FromResult(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, "resumo da conversa até aqui")));
            }

            // Turno do agente. A primeira chamada do turno pede a tool; a
            // segunda — já com o resultado dela na lista — responde texto.
            if (list.Count > 0 && list[^1].Role == ChatRole.Tool)
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            }

            lock (_gate)
            {
                EntradasPorTurno.Add(TamanhoDoConteudo(list));
            }

            var call = new FunctionCallContent(
                Guid.NewGuid().ToString("N")[..8],
                "consultar_saldo",
                new Dictionary<string, object?> { ["conta"] = "123" });

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Constrói um mock de <see cref="IChatClient"/> capaz de distinguir a
    /// chamada de resumo do <c>SummarizationCompactionStrategy</c> (sempre
    /// invocada com <c>ChatOptions</c> nulo — confirmado decompilando
    /// <c>CompactCoreAsync</c>: <c>ChatClient.GetResponseAsync(list, null,
    /// cancellationToken)</c>) da chamada de conversa real do
    /// <c>ChatClientAgent</c> (sempre invocada com <c>ChatOptions</c>
    /// não-nulo, construído a partir de <c>ChatClientAgentOptions.ChatOptions</c>).
    /// </summary>
    private static Mock<IChatClient> BuildSummarizingChatClientMock(
        List<List<ChatMessage>> summaryCallInputs,
        out Func<List<ChatMessage>?> mainCallCapture,
        string summaryText = "resumo simulado da conversa",
        string mainReplyText = "ok")
    {
        var mock = new Mock<IChatClient>();
        List<ChatMessage>? lastMainCallMessages = null;

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o == null),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, _, _) => summaryCallInputs.Add(messages.ToList()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, summaryText)));

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, _, _) => lastMainCallMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, mainReplyText)));

        mainCallCapture = () => lastMainCallMessages;
        return mock;
    }

    private IHost BuildHost(
        IChatClient chatClient,
        IMcpToolSetResolver? mcpToolSetResolver = null,
        List<(LogLevel Level, string Message)>? capturedLogs = null)
    {
        var builder = Host.CreateApplicationBuilder();

        if (capturedLogs is not null)
        {
            builder.Logging.AddProvider(new CapturingLoggerProvider(capturedLogs));
        }

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
        builder.Services.AddSingleton(mcpToolSetResolver ?? new NullMcpToolSetResolver());
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

    private async Task SeedAgentAsync(Guid agentId, string agentName = "Atendente", string instructions = "Responda com simpatia.", string provider = "openai", string model = "gpt-5.6-sol")
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

    private async Task SeedTaskAsync(string taskId, Guid agentId, string contextId, string userMessage)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options;
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

    /// <summary>
    /// Seed + publish + poll de um único turno — reduz repetição nos loops
    /// multi-turno acima.
    /// </summary>
    private async Task<A2ATaskRecord> RunTurnAsync(Guid agentId, string contextId, string userMessage)
    {
        var taskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(taskId, agentId, contextId, userMessage);
        await PublishJobAsync(taskId, agentId, contextId);
        return await PollUntilTerminalAsync(taskId);
    }
}
