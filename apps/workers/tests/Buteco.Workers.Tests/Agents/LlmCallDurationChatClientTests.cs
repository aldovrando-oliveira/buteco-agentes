using System.Net;
using Buteco.Workers.Agents;
using Buteco.Workers.ExecutionMetrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Tests.Agents;

/// <summary>
/// Afirma o instrumento de diagnóstico criado pela change
/// <c>fix-vazamento-httpclient-chat</c>: a duração de cada requisição ao
/// provedor de LLM é registrada, inclusive quando a requisição falha.
/// </summary>
public class LlmCallDurationChatClientTests
{
    private const string Provider = "gemini";
    private const string Model = "gemini-3.6-flash";

    [Fact]
    public async Task GetResponseAsync_OnSuccess_LogsDurationWithProviderAndModel()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "oi"))));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(Provider, entry.Message);
        Assert.Contains(Model, entry.Message);
        Assert.Contains("duracaoMs", entry.Message);
    }

    /// <summary>
    /// O caso que mais importa para o defeito que originou esta classe: a
    /// chamada que estoura por espera de conexão do pool NÃO pode ficar sem
    /// medida registrada. A exceção precisa continuar propagando.
    /// </summary>
    [Fact]
    public async Task GetResponseAsync_WhenInnerThrows_LogsDurationAndRethrows()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => throw new TimeoutException("espera por conexão do pool"));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        await Assert.ThrowsAsync<TimeoutException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]));

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(Provider, entry.Message);
        Assert.Contains("duracaoMs", entry.Message);
    }

    [Fact]
    public async Task GetResponseAsync_ReturnsInnerResponseUnchanged()
    {
        var expected = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris"));
        var inner = new StubChatClient(_ => Task.FromResult(expected));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, new CapturingLogger());

        var actual = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "capital?")]);

        Assert.Same(expected, actual);
    }

    /// <summary>
    /// No streaming a medida cobre a enumeração inteira — é a leitura dos
    /// updates que ocupa a conexão, não a obtenção do enumerador. Por isso o log
    /// só sai depois do último update, e não antes do primeiro.
    /// </summary>
    [Fact]
    public async Task GetStreamingResponseAsync_LogsOnlyAfterEnumerationCompletes()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => throw new InvalidOperationException("não usado"))
        {
            StreamingUpdates = ["a", "b"],
        };
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        var received = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "olá")]))
        {
            Assert.Empty(logger.Entries);
            received.Add(update.Text);
        }

        Assert.Equal(["a", "b"], received);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("streaming=True", entry.Message);
    }

    // ── Métricas de execução (change metricas-execucao-coleta) ───────────
    // ── Escopo 5 da change compactacao-historico (D7) ────────────────────
    //
    // Casar log com tabela dependeu de as durações serem únicas: as seis
    // chamadas de compactação do piloto só foram identificadas no log porque
    // 342,0 / 373,2 / 488,5 / 519,8 / 395,4 / 368,5 ms não se repetiam. A
    // próxima vez pode não dar.

    [Fact]
    public async Task OnSuccess_InsideExecutionScope_LogsTaskAndPurpose()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "oi"))));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        using var scope = ExecutionMetricsScope.Begin("task-do-turno");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("task-do-turno", entry.Message, StringComparison.Ordinal);
        Assert.Contains(ExecutionMetricsValues.Purpose.Turn, entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A linha de falha é <b>textualmente distinta</b> da de sucesso. Até esta
    /// change as duas eram a mesma — escrita num <c>finally</c>, sem campo de
    /// resultado —, e por isso o log do piloto mostrava "Chamada ao LLM
    /// concluída" para seis chamadas que tinham falhado.
    /// </summary>
    [Fact]
    public async Task OnFailure_LogsDistinctLineWithExceptionTypeAndStatus()
    {
        var logger = new CapturingLogger();
        var sucesso = new StubChatClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "oi"))));
        using (var okClient = new LlmCallDurationChatClient(sucesso, Provider, Model, logger))
        {
            await okClient.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);
        }

        var inner = new StubChatClient(_ => throw new HttpRequestException(
            "limite", null, HttpStatusCode.TooManyRequests));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        using var scope = ExecutionMetricsScope.Begin("task-que-falha");
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]));

        var linhaSucesso = logger.Entries[0];
        var linhaFalha = logger.Entries[1];

        Assert.NotEqual(linhaSucesso.Message, linhaFalha.Message);
        Assert.Contains(nameof(HttpRequestException), linhaFalha.Message, StringComparison.Ordinal);
        Assert.Contains("429", linhaFalha.Message, StringComparison.Ordinal);
        Assert.Contains("task-que-falha", linhaFalha.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompactionCall_LogsPurposeCompaction()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "resumo"))));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);
        using var marcado = new Buteco.Workers.ExecutionMetrics.CompactionCallChatClient(client);

        using var scope = ExecutionMetricsScope.Begin("task-com-resumo");
        await marcado.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(ExecutionMetricsValues.Purpose.Compaction, entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Fora de execução de task não há identificador para registrar — e a linha
    /// continua saindo, porque o client é usado fora de execução nos testes dele
    /// e em qualquer caminho futuro que não seja task.
    /// </summary>
    [Fact]
    public async Task OutsideExecutionScope_StillLogs_WithoutTaskIdentifier()
    {
        var logger = new CapturingLogger();
        var inner = new StubChatClient(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "oi"))));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, logger);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(Provider, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("task-", entry.Message, StringComparison.Ordinal);
    }

    // O client é o ÚNICO ponto que vê cada requisição HTTP ao provedor
    // separada do tempo das tools (comentário da classe), então é aqui que a
    // linha filha nasce. Os três guardas abrem o escopo à mão, como
    // AgentExecutionService abre em produção.

    [Fact]
    public async Task GetResponseAsync_InsideExecutionScope_RecordsCallWithReportedTokens()
    {
        var usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30, CachedInputTokenCount = null };
        var inner = new StubChatClient(_ => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "oi")) { Usage = usage }));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, new CapturingLogger());

        using var scope = ExecutionMetricsScope.Begin("task-turno");
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]);

        var call = Assert.Single(scope.ProviderCalls);
        Assert.Equal("task-turno", call.TaskId);
        Assert.Equal(Provider, call.Provider);
        Assert.Equal(Model, call.Model);
        Assert.Equal(ExecutionMetricsValues.Purpose.Turn, call.Purpose);
        Assert.Equal(120, call.InputTokens);
        Assert.Equal(30, call.OutputTokens);
        Assert.Null(call.CachedInputTokens);
        Assert.False(call.Failed);
    }

    /// <summary>
    /// D9: o caminho de streaming é morto no fluxo do agente hoje, e mesmo assim
    /// é instrumentado — deixar tokens só no outro caminho seria a assimetria
    /// pronta para enganar quem ligar streaming. No streaming os tokens chegam
    /// como <see cref="UsageContent"/> dentro dos updates, não num campo da
    /// resposta.
    /// </summary>
    [Fact]
    public async Task GetStreamingResponseAsync_InsideExecutionScope_RecordsTokensFromUsageContent()
    {
        var inner = new StubChatClient(_ => throw new InvalidOperationException("não usado"))
        {
            StreamingUpdates = ["a", "b"],
            StreamingUsage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 7 },
        };
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, new CapturingLogger());

        using var scope = ExecutionMetricsScope.Begin("task-streaming");
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "olá")]))
        {
        }

        var call = Assert.Single(scope.ProviderCalls);
        Assert.Equal(50, call.InputTokens);
        Assert.Equal(7, call.OutputTokens);
        Assert.Null(call.CachedInputTokens);
    }

    [Fact]
    public async Task GetResponseAsync_WhenInnerThrowsHttpError_RecordsFailedCallWithStatus_AndRethrows()
    {
        var inner = new StubChatClient(_ => throw new HttpRequestException("limite", null, HttpStatusCode.TooManyRequests));
        using var client = new LlmCallDurationChatClient(inner, Provider, Model, new CapturingLogger());

        using var scope = ExecutionMetricsScope.Begin("task-falha");
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "olá")]));

        var call = Assert.Single(scope.ProviderCalls);
        Assert.True(call.Failed);
        Assert.Equal(429, call.HttpStatus);
        Assert.Null(call.InputTokens);
    }

    private sealed class StubChatClient(Func<IEnumerable<ChatMessage>, Task<ChatResponse>> onGetResponse) : IChatClient
    {
        public string[] StreamingUpdates { get; init; } = [];

        public UsageDetails? StreamingUsage { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => onGetResponse(messages);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var text in StreamingUpdates)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            }

            if (StreamingUsage is not null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(StreamingUsage)]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<LlmCallDurationChatClient>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
