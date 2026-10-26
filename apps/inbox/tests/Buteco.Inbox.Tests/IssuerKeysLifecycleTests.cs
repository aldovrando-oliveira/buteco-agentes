using System.Net;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Ciclo de vida das chaves do emissor (spec <c>issuer-token-validation</c>; design.md da
/// change validacao-jwks-inbox-connectors, D3 e D4): o app sobe e atende sem o
/// <c>apps/api</c>, o token do emissor recebe <c>503</c> até a primeira obtenção, o cache
/// sobrevive à queda, e a busca por <c>kid</c> desconhecido é limitada. Uma factory por
/// teste, porque o estado das chaves é do processo.
/// </summary>
public class IssuerKeysLifecycleTests(PostgresOnlyFixture postgres) : IClassFixture<PostgresOnlyFixture>
{
    [Fact]
    public async Task BootsWithTheIssuerUnreachable_HmacWorks_AndIssuerTokenIsUnavailable()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new IssuerTokenFactory(postgres, issuer);

        var health = await factory.CreateClientWithToken(null).GetAsync("/health");
        var hmac = await factory.CreateClientWithToken(factory.OperatorHmacToken()).GetAsync("/channels");
        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hmac.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, issued.StatusCode);
        Assert.Equal("10", issued.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task IssuerAnsweringWith503_IsAlsoUnavailable()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.ServiceUnavailable };
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests >= 1, "a primeira tentativa não aconteceu");

        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, issued.StatusCode);
    }

    [Fact]
    public async Task RecoversWithoutRestart_WhenTheIssuerComesBack()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new IssuerTokenFactory(postgres, issuer, new IssuerKeysSchedule
        {
            FirstFetchRetryInterval = TimeSpan.FromMilliseconds(200),
            FailedFetchBackoff = TimeSpan.FromMilliseconds(100),
        });
        var client = factory.CreateClientWithToken(issuer.Issue());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/channels")).StatusCode);

        issuer.Availability = IssuerAvailability.Online;
        await factory.WaitForKeysAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/channels")).StatusCode);
    }

    [Fact]
    public async Task IssuerDownAfterTheKeysWereObtained_TokensStillValidate()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();

        issuer.Availability = IssuerAvailability.Unreachable;
        RequestRefresh(factory);
        await Poll.UntilAsync(() => issuer.Requests.Count >= 3, "a renovação pedida não chegou ao emissor");

        var response = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Produção: 10 s de espera entre falhas. A primeira tentativa em segundo plano falha
    // no start; nenhuma das vinte requisições seguintes pode virar busca nova.
    [Fact]
    public async Task BurstWithTheIssuerUnreachable_DoesNotMultiplyFetches()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests == 1, "a primeira tentativa não aconteceu");
        var token = issuer.Issue();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => factory.CreateClientWithToken(token).GetAsync("/channels")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode));
        Assert.Equal(1, issuer.DiscoveryRequests);
    }

    [Fact]
    public async Task NewKeyOutsideTheCache_IsUnauthorizedOnce_ThenAccepted()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();
        var newKey = TestIssuer.CreateKey();
        issuer.Publish(newKey);
        var client = factory.CreateClientWithToken(issuer.Issue(key: newKey));
        var jwksBefore = issuer.JwksRequests;

        var first = await client.GetAsync("/channels");
        await Poll.UntilAsync(async () => (await client.GetAsync("/channels")).StatusCode == HttpStatusCode.OK, "a chave nova não foi aceita");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(jwksBefore + 1, issuer.JwksRequests);
    }

    [Fact]
    public async Task InventedKids_DoNotBecomeAFetchBurst()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();
        var jwksBefore = issuer.JwksRequests;

        var first = await factory.CreateClientWithToken(issuer.Issue(key: TestIssuer.CreateKey())).GetAsync("/channels");
        await Poll.UntilAsync(() => issuer.JwksRequests == jwksBefore + 1, "o kid desconhecido não disparou busca");
        var second = await factory.CreateClientWithToken(issuer.Issue(key: TestIssuer.CreateKey())).GetAsync("/channels");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(jwksBefore + 1, issuer.JwksRequests);
    }

    // A chave retirada é recusada assim que o cache é renovado: sem o
    // UseLastKnownGoodConfiguration = false, a configuração anterior a validaria por até 1 h.
    [Fact]
    public async Task RotatedKey_BothValidWhilePublished_OldOneRefusedAfterRetired()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer, new IssuerKeysSchedule
        {
            UnknownKidRefreshInterval = TimeSpan.FromSeconds(1),
        });
        await factory.WaitForKeysAsync();
        var oldClient = factory.CreateClientWithToken(issuer.Issue());
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/channels")).StatusCode);

        var newKey = TestIssuer.CreateKey();
        issuer.Publish(newKey);
        var newClient = factory.CreateClientWithToken(issuer.Issue(key: newKey));
        await Poll.UntilAsync(async () => (await newClient.GetAsync("/channels")).StatusCode == HttpStatusCode.OK, "a chave nova não foi aceita");
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync("/channels")).StatusCode);

        issuer.Retire(issuer.SigningKey);
        var jwksBefore = issuer.JwksRequests;
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        RequestRefresh(factory);
        await Poll.UntilAsync(() => issuer.JwksRequests > jwksBefore, "a renovação pedida não aconteceu");
        await Poll.UntilAsync(async () => (await oldClient.GetAsync("/channels")).StatusCode == HttpStatusCode.Unauthorized, "a chave retirada continuou valendo");

        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync("/channels")).StatusCode);
    }

    [Fact]
    public async Task DivergentIssuerInDiscovery_KeepsTheKeysOut_AndLogsBothValues()
    {
        const string announced = "https://outro.exemplo.test/";
        var issuer = new TestIssuer { AnnouncedIssuer = announced };
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests >= 1, "a primeira tentativa não aconteceu");

        var response = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, issuer.JwksRequests);
        Assert.Contains(factory.Logs.ToList(), entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains(announced, StringComparison.Ordinal)
            && entry.Message.Contains(TestIssuer.Issuer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeysAreFetchedOnlyFromTheInternalAddress()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();

        Assert.Equal(
            [
                new Uri(TestIssuer.InternalBaseUrl + IssuerKeysRetriever.DiscoveryPath),
                new Uri(TestIssuer.InternalBaseUrl + IssuerKeysRetriever.JwksPath),
            ],
            issuer.Requests);
    }

    private static void RequestRefresh(IssuerTokenFactory factory) =>
        factory.Services.GetRequiredService<IConfigurationManager<OpenIdConnectConfiguration>>().RequestRefresh();
}
