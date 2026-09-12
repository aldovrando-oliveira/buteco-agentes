using Buteco.Workers.Agents;
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

    private sealed class StubChatClient(Func<IEnumerable<ChatMessage>, Task<ChatResponse>> onGetResponse) : IChatClient
    {
        public string[] StreamingUpdates { get; init; } = [];

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
