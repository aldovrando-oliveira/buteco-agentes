using System.Reflection;
using Buteco.Api.Identity.ServiceTokens;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenIddict.Abstractions;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Superfície estreita, cache e linha de log do <see cref="ServiceAccessTokenIssuer"/> (spec
/// <c>service-client-credentials</c>; design.md da change servicos-token-emissor, D2). O
/// gerador é construído aqui com um <see cref="FakeTimeProvider"/>, sobre o OpenIddict real da
/// fixture, para o relógio do cache andar sem esperar.
/// </summary>
public class ServiceAccessTokenIssuerCacheTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    // Uma requisição passa pelo gate e carrega o anel antes de qualquer geração.
    public async Task InitializeAsync() =>
        Assert.True((await factory.CreateClient().GetAsync("/.well-known/jwks")).IsSuccessStatusCode);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void ThePublicSurface_TakesNothingButTheCancellationToken()
    {
        var methods = typeof(ServiceAccessTokenIssuer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        Assert.Equal(["GetAsync"], methods.Select(method => method.Name));
        Assert.All(methods, method => Assert.All(method.GetParameters(), parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType)));
    }

    [Fact]
    public async Task TheToken_IsReused_UntilOneMinuteBeforeItExpires()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var issuer = NewIssuer(clock, []);

        var first = await issuer.GetAsync(CancellationToken.None);
        Assert.Equal(first, await issuer.GetAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromSeconds(839));
        Assert.Equal(first, await issuer.GetAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(first, await issuer.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheLine_IsLoggedForTheFirstToken_AndAfterAnInvalidation_NotForEachRenewal()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var lines = new List<string>();
        var issuer = NewIssuer(clock, lines);

        await issuer.GetAsync(CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(15));
        var renewed = await issuer.GetAsync(CancellationToken.None);
        Assert.Single(lines);

        // O 401 do destino passa pelo handler, que é quem invalida.
        using var invoker = new HttpMessageInvoker(new Buteco.Api.Auth.ServiceTokenDelegatingHandler(issuer)
        {
            InnerHandler = new StatusHandler(System.Net.HttpStatusCode.Unauthorized),
        });
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/x"), CancellationToken.None);

        Assert.NotEqual(renewed, await issuer.GetAsync(CancellationToken.None));
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Contains("buteco-servico-api", line));
        Assert.DoesNotContain(lines, line => line.Contains(renewed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Generating_WritesNoTokenRow()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var before = await tokens.CountAsync();

        await NewIssuer(new FakeTimeProvider(DateTimeOffset.UtcNow), []).GetAsync(CancellationToken.None);

        Assert.Equal(before, await tokens.CountAsync());
    }

    private ServiceAccessTokenIssuer NewIssuer(TimeProvider clock, List<string> lines) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(lines))).CreateLogger<ServiceAccessTokenIssuer>());

    private sealed class StatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ListLoggerProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Information)
                {
                    lock (lines)
                    {
                        lines.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}
