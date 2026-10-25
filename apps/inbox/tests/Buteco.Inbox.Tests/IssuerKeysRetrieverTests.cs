using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Unidade do <see cref="IssuerKeysRetriever"/> (design.md da change
/// validacao-jwks-inbox-connectors, D3 e D4): o issuer do discovery conferido, a espera
/// entre buscas falhas e o <see cref="IssuerKeysState.Obtained"/>, com o relógio na mão.
/// </summary>
public class IssuerKeysRetrieverTests
{
    private readonly TestIssuer _issuer = new();
    private readonly FakeTimeProvider _time = new();
    private readonly IssuerKeysState _state = new();
    private readonly List<CapturedLogEntry> _logs = [];

    [Fact]
    public async Task Success_ReturnsTheConfiguredIssuerAndThePublishedKeys_AndMarksObtained()
    {
        var configuration = await GetAsync(CreateRetriever());

        Assert.Equal(TestIssuer.Issuer, configuration.Issuer);
        Assert.Equal(_issuer.SigningKey.KeyId, Assert.Single(configuration.SigningKeys).KeyId);
        Assert.True(_state.Obtained);
        Assert.Contains(_logs, entry => entry.Level == LogLevel.Information && entry.Message.Contains(_issuer.SigningKey.KeyId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DivergentIssuer_Fails_LogsBothValues_AndStaysNotObtained()
    {
        _issuer.AnnouncedIssuer = "https://outro.exemplo.test/";

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(CreateRetriever()));

        Assert.Contains("https://outro.exemplo.test/", exception.Message, StringComparison.Ordinal);
        Assert.Contains(TestIssuer.Issuer, exception.Message, StringComparison.Ordinal);
        Assert.Contains(_logs, entry => entry.Level == LogLevel.Error && entry.Message == exception.Message);
        Assert.Equal(0, _issuer.JwksRequests);
        Assert.False(_state.Obtained);
    }

    [Fact]
    public async Task EmptyJwks_Fails_AndStaysNotObtained()
    {
        _issuer.Retire(_issuer.SigningKey);

        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(CreateRetriever()));

        Assert.False(_state.Obtained);
    }

    [Fact]
    public async Task WithinTheBackoffAfterAFailure_FailsWithoutHttp_ThenFetchesAgain()
    {
        var retriever = CreateRetriever();
        _issuer.Availability = IssuerAvailability.Unreachable;
        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(retriever));
        Assert.Single(_issuer.Requests);
        _issuer.Availability = IssuerAvailability.Online;

        _time.Advance(TimeSpan.FromSeconds(9));
        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(retriever));
        Assert.Single(_issuer.Requests);

        _time.Advance(TimeSpan.FromSeconds(1));
        await GetAsync(retriever);
        Assert.Equal(3, _issuer.Requests.Count);
        Assert.True(_state.Obtained);
    }

    [Fact]
    public async Task FetchFailure_IsLoggedAsWarning_NamingTheKeyWithoutEchoingTheAddress()
    {
        _issuer.Availability = IssuerAvailability.Unreachable;

        await Assert.ThrowsAnyAsync<Exception>(() => GetAsync(CreateRetriever()));

        var warning = Assert.Single(_logs, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("Api:BaseUrl", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestIssuer.InternalBaseUrl, warning.Message, StringComparison.Ordinal);
    }

    private static Task<OpenIdConnectConfiguration> GetAsync(IssuerKeysRetriever retriever) =>
        retriever.GetConfigurationAsync("ignorado", new HttpDocumentRetriever(), CancellationToken.None);

    private IssuerKeysRetriever CreateRetriever()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(IssuerKeysRetriever.HttpClientName, client => client.BaseAddress = new Uri(TestIssuer.InternalBaseUrl))
            .ConfigurePrimaryHttpMessageHandler(_issuer.CreateHandler);
        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(new CapturingLoggerProvider(_logs, "Buteco.Inbox.Auth")));

        return new IssuerKeysRetriever(
            httpClientFactory,
            Microsoft.Extensions.Options.Options.Create(new IssuerTokenOptions { Issuer = TestIssuer.Issuer }),
            _state,
            new IssuerKeysSchedule(),
            _time,
            loggerFactory.CreateLogger<IssuerKeysRetriever>());
    }
}

/// <summary>
/// Unidade do <see cref="IssuerKeysRefreshService"/> (D4): tentativa a cada 10 s até
/// obter, depois o tique de 10 min, e a falha de cada iteração capturada (convenção 4).
/// </summary>
public class IssuerKeysRefreshServiceTests
{
    [Fact]
    public async Task RetriesEveryTenSecondsUntilObtained_ThenTicksEveryTenMinutes_SurvivingFailures()
    {
        var time = new FakeTimeProvider();
        var state = new IssuerKeysState();
        var manager = new ScriptedConfigurationManager(state, failuresBeforeSuccess: 2);
        using var service = new IssuerKeysRefreshService(manager, state, new IssuerKeysSchedule(), time, NullLogger<IssuerKeysRefreshService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Poll.UntilAsync(() => manager.Calls == 1, "a primeira tentativa não aconteceu no start");

        await AdvanceAndExpectAsync(time, manager, TimeSpan.FromSeconds(9.9), expectedCalls: 1);
        await AdvanceAndExpectAsync(time, manager, TimeSpan.FromSeconds(0.1), expectedCalls: 2);
        await AdvanceAndExpectAsync(time, manager, TimeSpan.FromSeconds(10), expectedCalls: 3);
        Assert.True(state.Obtained);

        await AdvanceAndExpectAsync(time, manager, TimeSpan.FromSeconds(10), expectedCalls: 3);
        await AdvanceAndExpectAsync(time, manager, TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(10), expectedCalls: 4);

        await service.StopAsync(CancellationToken.None);
    }

    private static async Task AdvanceAndExpectAsync(FakeTimeProvider time, ScriptedConfigurationManager manager, TimeSpan by, int expectedCalls)
    {
        time.Advance(by);
        if (manager.Calls != expectedCalls)
        {
            await Poll.UntilAsync(() => manager.Calls == expectedCalls, $"esperava {expectedCalls} chamadas, houve {manager.Calls}");
        }

        // Dá tempo de uma chamada a mais aparecer, se o serviço fosse chamar antes da hora.
        await Task.Delay(50);
        Assert.Equal(expectedCalls, manager.Calls);
    }

    private sealed class ScriptedConfigurationManager(IssuerKeysState state, int failuresBeforeSuccess)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            if (Interlocked.Increment(ref _calls) <= failuresBeforeSuccess)
            {
                throw new InvalidOperationException("emissor fora (teste)");
            }

            state.MarkObtained();
            return Task.FromResult(new OpenIdConnectConfiguration());
        }

        public void RequestRefresh()
        {
        }
    }
}
