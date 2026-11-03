using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Buteco.Connectors.Tests;

// Duplicado deliberadamente de
// apps/inbox/tests/Buteco.Inbox.Tests/ServiceAccessTokenSourceTests.cs — a mesma
// lista de casos nas duas cópias do source (design.md da change servicos-token-emissor, D9).

/// <summary>
/// O token de serviço do <c>apps/connectors</c> contra o duplo do <c>/connect/token</c> (spec
/// <c>service-client-credentials</c>; design.md da change servicos-token-emissor, D3 e D9).
/// Sem contêiner. Cada caso tem o defeito que existe para pegar no registro do tasks.md (4.2).
/// </summary>
public class ServiceAccessTokenSourceTests
{
    private const string ApiBaseUrl = "http://api-interno.test:8080";

    // Com "+", "/" e "=": o achado da #234 (o "+" mandado sem codificação chega como espaço).
    private const string Secret = "a+b/c+d/e+f/g+h/i+j/k+l/m+n/o+p/q+r/s+t/u+v=";

    private readonly FakeTokenEndpoint _endpoint = new();
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly List<LogEntry> _logs = [];

    [Fact]
    public async Task TwoCalls_MakeOneRequest()
    {
        var source = NewSource();

        var first = await source.GetAsync(CancellationToken.None);
        var second = await source.GetAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, _endpoint.Requests);
    }

    [Fact]
    public async Task TheToken_IsReused_UntilOneMinuteBeforeItExpires()
    {
        var source = NewSource();
        var first = await source.GetAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(839));
        Assert.Equal(first, await source.GetAsync(CancellationToken.None));
        Assert.Equal(1, _endpoint.Requests);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(first, await source.GetAsync(CancellationToken.None));
        Assert.Equal(2, _endpoint.Requests);
    }

    [Fact]
    public async Task TenConcurrentCalls_MakeOneRequest_AndShareTheToken()
    {
        var source = NewSource();
        var release = _endpoint.Hold();

        var calls = Enumerable.Range(0, 10).Select(_ => Task.Run(() => source.GetAsync(CancellationToken.None).AsTask())).ToArray();
        await _endpoint.WaitForRequestsAsync(1);
        // As outras nove já estão esperando o semáforo ou o resultado do primeiro pedido.
        await Task.Delay(200);
        release.SetResult();
        var tokens = await Task.WhenAll(calls);

        Assert.Equal(1, _endpoint.Requests);
        Assert.Single(tokens.Distinct());
    }

    [Fact]
    public async Task TheForm_ReachesTheEndpointIntact()
    {
        var source = NewSource();

        await source.GetAsync(CancellationToken.None);

        var form = Assert.Single(_endpoint.Forms);
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal("buteco-servico-connectors", form["client_id"]);
        Assert.Equal(Secret, form["client_secret"]);
        Assert.Equal("buteco-api", form["scope"]);
    }

    [Fact]
    public async Task ServiceUnavailable_FailsAsHttpRequestException_WithAWarning()
    {
        var source = NewSource();
        _endpoint.RespondStatus(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());

        var warning = Assert.Single(_logs, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("buteco-servico-connectors", warning.Message);
        Assert.Contains("503", warning.Message);
    }

    [Fact]
    public async Task InvalidClient_FailsAsHttpRequestException_WithAnError_WithoutTheSecretOrTheAddress()
    {
        var source = NewSource();
        _endpoint.RespondInvalidClient();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());

        var error = Assert.Single(_logs, entry => entry.Level == LogLevel.Error);
        Assert.Contains("buteco-servico-connectors", error.Message);
        Assert.Contains("invalid_client", error.Message);
        Assert.DoesNotContain(_logs, entry => entry.Level >= LogLevel.Warning && entry.Level != LogLevel.Error);
        Assert.All(_logs, entry =>
        {
            Assert.DoesNotContain(Secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("api-interno.test", entry.Message, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAnswer_FailsAtTheScheduleTimeout_AsHttpRequestException()
    {
        var source = NewSource(new ServiceAccessTokenSchedule { RequestTimeout = TimeSpan.FromMilliseconds(300) });
        _endpoint.Hang();

        var call = source.GetAsync(CancellationToken.None).AsTask();
        var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(call, finished);
        await Assert.ThrowsAsync<HttpRequestException>(() => call);
    }

    [Fact]
    public async Task TheCallersCancellation_StaysAnOperationCanceledException()
    {
        var source = NewSource();
        _endpoint.Hang();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(cancellation.Token).AsTask());

        Assert.IsNotType<HttpRequestException>(exception);
    }

    [Fact]
    public async Task AfterAFailure_CallsFailWithoutARequest_UntilTheBackoffPasses()
    {
        var source = NewSource();
        _endpoint.RespondStatus(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());

        _clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());
        Assert.Equal(1, _endpoint.Requests);

        _clock.Advance(TimeSpan.FromSeconds(6));
        await source.GetAsync(CancellationToken.None);
        Assert.Equal(2, _endpoint.Requests);
    }

    [Fact]
    public async Task TheObtainedLine_IsLoggedForTheFirstToken_AndForTheRecovery_Only()
    {
        var source = NewSource();

        await source.GetAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(15));
        await source.GetAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(15));
        _endpoint.RespondStatus(HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());
        _clock.Advance(TimeSpan.FromSeconds(11));
        var recovered = await source.GetAsync(CancellationToken.None);

        var obtained = _logs.Where(entry => entry.Level == LogLevel.Information).ToList();
        Assert.Equal(2, obtained.Count);
        Assert.All(obtained, entry => Assert.Contains("buteco-servico-connectors", entry.Message));
        Assert.DoesNotContain(obtained, entry => entry.Message.Contains(recovered, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"token_type":"Bearer","expires_in":900}""")]
    [InlineData("""{"access_token":"sem-validade","token_type":"Bearer"}""")]
    public async Task AnIncompleteAnswer_FailsLikeTheIssuerDown_AndCachesNothing(string body)
    {
        var source = NewSource();
        _endpoint.RespondStatus(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(CancellationToken.None).AsTask());

        _clock.Advance(TimeSpan.FromSeconds(11));
        var token = await source.GetAsync(CancellationToken.None);
        Assert.StartsWith("token-de-servico-", token);
        Assert.Equal(2, _endpoint.Requests);
    }

    private ServiceAccessTokenSource NewSource(ServiceAccessTokenSchedule? schedule = null) =>
        new(
            new SingleClientFactory(_endpoint),
            Microsoft.Extensions.Options.Options.Create(new ServiceClientOptions { Secret = Secret }),
            schedule ?? new ServiceAccessTokenSchedule(),
            _clock,
            LoggerFactory.Create(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddProvider(new ListLoggerProvider(_logs)))
                .CreateLogger<ServiceAccessTokenSource>());

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLoggerProvider(List<LogEntry> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(entries);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add(new LogEntry(logLevel, formatter(state, exception)));
                }
            }
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ServiceAccessTokenSource.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri(ApiBaseUrl),
                Timeout = Timeout.InfiniteTimeSpan,
            };
        }
    }
}
