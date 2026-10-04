using System.Diagnostics;
using Buteco.ProviderCatalog;
using Buteco.Workers.Agents;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Buteco.Workers.Tests.Http;

/// <summary>
/// Guardas do timeout de conexão das chamadas de saída que seguram o lock de
/// contexto (change timeout-de-conexao-saida-workers, #46). Cada um constrói o
/// cliente pelo RESOLVEDOR DE PRODUÇÃO e o aponta para um
/// <see cref="SilentTlsListener"/>: a conexão TCP abre e o TLS nunca completa.
///
/// <para>
/// <b>O orçamento é o lock.</b> A mensagem seguinte da conversa desiste do lock em
/// 30 s (<c>CommandTimeout</c>). Sem timeout de conexão, o chat OpenAI esperava
/// 4 × 100 s e o embedding também (medido em 03/10/2026 sobre <c>a7960d9</c>). Com
/// 5 s por tentativa, as 4 tentativas do <c>System.ClientModel</c> dão ~20 s.
/// </para>
/// </summary>
public class OutboundConnectTimeoutTests
{
    // O limite é o do REQUISITO, não o medido: a mensagem seguinte desiste do lock
    // em 30 s. Medido com a correção: 20,1 s (chat) e 20,3 s (embedding), 4
    // tentativas × 5 s. Um limite de 25 s, colado no medido, reprovou o embedding
    // aos 25,6 s numa rodada com a VM disputada; o que o guarda protege é "antes
    // do lock", e a TimeoutException do ConnectTimeout na cadeia é o que prova que
    // foi o timeout de conexão que cortou. Sem a correção: 4 × 100 s.
    private static readonly TimeSpan OpenAiLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task OpenAiChat_WhenConnectionNeverCompletes_FailsWithConnectTimeoutWithinTheLockBudget()
    {
        using var listener = new SilentTlsListener();
        var resolver = new ChatClientResolver(
            MsOptions.Create(new ChatClientOptions { BaseUrl = $"{listener.Url}/v1", ApiKey = "fake" }),
            MsOptions.Create(new AnthropicOptions()),
            MsOptions.Create(new GeminiOptions()),
            NullLoggerFactory.Instance);
        var chatClient = resolver.Resolve(LlmProviders.OpenAi, "gpt-x");

        await ConnectTimeoutAssert.FailsWithinAsync(
            ct => chatClient.GetResponseAsync("oi", cancellationToken: ct), OpenAiLimit);
    }

    /// <summary>
    /// Também prende o TIPO do topo da exceção: <c>AggregateException</c> da
    /// <c>ClientRetryPolicy</c>, não <c>OperationCanceledException</c>. É dele que
    /// dependem os <c>catch (... when (exception is not OperationCanceledException))</c>
    /// de <c>KnowledgeToolSetResolver</c> e <c>KnowledgeIndexingService</c>: se o
    /// embedding passasse a chegar como cancelamento, a tool de conhecimento
    /// derrubaria a execução em vez de degradar (design.md, D4).
    /// </summary>
    [Fact]
    public async Task OpenAiEmbedding_WhenConnectionNeverCompletes_FailsWithConnectTimeoutWithinTheLockBudget()
    {
        using var listener = new SilentTlsListener();
        var resolver = new EmbeddingGeneratorResolver(
            MsOptions.Create(new ChatClientOptions { BaseUrl = $"{listener.Url}/v1", ApiKey = "fake" }),
            MsOptions.Create(new EmbeddingOptions { Provider = "openai", Model = "modelo-x", Dimensions = 8 }));
        var generator = resolver.Resolve();

        var exception = await ConnectTimeoutAssert.FailsWithinAsync(
            ct => generator.GenerateAsync(["oi"], cancellationToken: ct), OpenAiLimit);

        Assert.IsNotAssignableFrom<OperationCanceledException>(exception);
    }
}

/// <summary>
/// Anthropic e Gemini só aceitam endpoint por variável de ambiente do processo
/// (<c>ANTHROPIC_BASE_URL</c>, <c>GOOGLE_GEMINI_BASE_URL</c>): as <c>Options</c> do
/// worker não têm <c>BaseUrl</c>, e acrescentar uma só para teste seria
/// configuração sem cenário. Daí esta collection sem paralelismo: enquanto a
/// variável aponta para o listener, nenhum outro teste roda.
///
/// <para>
/// <b>Por que o cliente medido nasce aqui e morre aqui</b> (design.md, D5): o cache
/// do <see cref="ChatClientResolver"/> é POR INSTÂNCIA, e cada teste constrói o
/// seu. O Anthropic lê a variável num <c>Lazy</c> avaliado na PRIMEIRA REQUISIÇÃO;
/// o Gemini, no construtor. Por isso a variável é definida antes do
/// <c>Resolve</c> e só restaurada depois de a chamada terminar. Restaurar entre os
/// dois mandaria o cliente Anthropic à API real.
/// </para>
/// </summary>
[Collection(Name)]
public class OutboundConnectTimeoutEnvironmentTests
{
    public const string Name = "outbound-connect-timeout-environment";

    // Uma tentativa: os dois SDKs não repetem o cancelamento do ConnectTimeout.
    // Medido: 5,2 s (Anthropic) e 5,3 s (Gemini).
    private static readonly TimeSpan SingleAttemptLimit = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Anthropic_WhenConnectionNeverCompletes_FailsWithConnectTimeout()
    {
        using var listener = new SilentTlsListener();

        await WithEnvironmentVariableAsync("ANTHROPIC_BASE_URL", listener.Url, async () =>
        {
            var chatClient = BuildResolver(anthropicApiKey: "fake").Resolve(LlmProviders.Anthropic, "claude-x");

            await ConnectTimeoutAssert.FailsWithinAsync(
                ct => chatClient.GetResponseAsync("oi", cancellationToken: ct), SingleAttemptLimit);
        });
    }

    [Fact]
    public async Task Gemini_WhenConnectionNeverCompletes_FailsWithConnectTimeout()
    {
        using var listener = new SilentTlsListener();

        await WithEnvironmentVariableAsync("GOOGLE_GEMINI_BASE_URL", listener.Url, async () =>
        {
            var chatClient = BuildResolver(geminiApiKey: "fake").Resolve(LlmProviders.Gemini, "gemini-x");

            await ConnectTimeoutAssert.FailsWithinAsync(
                ct => chatClient.GetResponseAsync("oi", cancellationToken: ct), SingleAttemptLimit);
        });
    }

    private static ChatClientResolver BuildResolver(string anthropicApiKey = "", string geminiApiKey = "") =>
        new(
            MsOptions.Create(new ChatClientOptions()),
            MsOptions.Create(new AnthropicOptions { ApiKey = anthropicApiKey }),
            MsOptions.Create(new GeminiOptions { ApiKey = geminiApiKey }),
            NullLoggerFactory.Instance);

    private static async Task WithEnvironmentVariableAsync(string name, string value, Func<Task> body)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}

[CollectionDefinition(OutboundConnectTimeoutEnvironmentTests.Name, DisableParallelization = true)]
public sealed class OutboundConnectTimeoutEnvironmentCollection
{
}

internal static class ConnectTimeoutAssert
{
    /// <summary>
    /// A chamada tem de FALHAR, antes de <paramref name="limit"/>, e com a
    /// <c>TimeoutException</c> do <c>ConnectTimeout</c> em algum ponto da cadeia
    /// (inclusive dentro das <c>InnerExceptions</c> de uma <c>AggregateException</c>
    /// de retentativa). Afirmar a mensagem, e não só o tempo, é o que separa "o
    /// timeout de conexão cortou" de "outro timeout calhou de caber no limite".
    /// </summary>
    public static async Task<Exception> FailsWithinAsync(Func<CancellationToken, Task> call, TimeSpan limit)
    {
        // Folga sobre o limite para que, sem a correção, o teste reprove pela
        // asserção de tempo e não fique pendurado o timeout total do SDK.
        using var budget = new CancellationTokenSource(limit + TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        Exception? thrown = null;
        try
        {
            await call(budget.Token);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        stopwatch.Stop();

        Assert.False(
            budget.IsCancellationRequested,
            $"A chamada ainda esperava conexão depois de {stopwatch.Elapsed.TotalSeconds:0.0} s; o limite é {limit.TotalSeconds:0} s.");
        Assert.NotNull(thrown);
        Assert.True(
            stopwatch.Elapsed < limit,
            $"Falhou em {stopwatch.Elapsed.TotalSeconds:0.0} s; o limite é {limit.TotalSeconds:0} s.");
        Assert.True(
            Flatten(thrown).Any(e => e is TimeoutException && e.Message.Contains("ConnectTimeout", StringComparison.Ordinal)),
            $"Nenhuma TimeoutException do ConnectTimeout na cadeia: {thrown}");

        return thrown;
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;

        var inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];

        foreach (var child in inner.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
