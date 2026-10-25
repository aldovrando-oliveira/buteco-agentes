using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Ciclo de vida das chaves do emissor (spec <c>issuer-token-validation</c>; design.md da
/// change validacao-jwks-inbox-connectors, D3 e D4), com as rotas do
/// <c>apps/connectors</c>. Uma factory por teste, porque o estado das chaves é do processo.
/// </summary>
public class IssuerKeysLifecycleTests
{
    private const string OperatorRoute = "/connectors/providers";

    [Fact]
    public async Task BootsWithTheIssuerUnreachable_HmacWorks_AndIssuerTokenIsUnavailable()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new ConnectorsFactory(issuer: issuer);

        var health = await factory.CreateClientWithToken(null).GetAsync("/health");
        var hmac = await factory.CreateClientWithToken(TestAuthentication.IssueToken(factory.Services, "operator")).GetAsync(OperatorRoute);
        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync(OperatorRoute);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hmac.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, issued.StatusCode);
        Assert.Equal("10", issued.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task IssuerAnsweringWith503_IsAlsoUnavailable()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.ServiceUnavailable };
        await using var factory = new ConnectorsFactory(issuer: issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests >= 1, "a primeira tentativa não aconteceu");

        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync(OperatorRoute);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, issued.StatusCode);
    }

    [Fact]
    public async Task RecoversWithoutRestart_WhenTheIssuerComesBack()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new ConnectorsFactory(issuer: issuer, issuerKeysSchedule: new IssuerKeysSchedule
        {
            FirstFetchRetryInterval = TimeSpan.FromMilliseconds(200),
            FailedFetchBackoff = TimeSpan.FromMilliseconds(100),
        });
        var client = factory.CreateClientWithToken(issuer.Issue());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(OperatorRoute)).StatusCode);

        issuer.Availability = IssuerAvailability.Online;
        await factory.WaitForIssuerKeysAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(OperatorRoute)).StatusCode);
    }

    [Fact]
    public async Task IssuerDownAfterTheKeysWereObtained_TokensStillValidate()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory(issuer: issuer);
        await factory.WaitForIssuerKeysAsync();

        issuer.Availability = IssuerAvailability.Unreachable;
        RequestRefresh(factory);
        await Poll.UntilAsync(() => issuer.Requests.Count >= 3, "a renovação pedida não chegou ao emissor");

        var response = await factory.CreateClientWithToken(issuer.Issue()).GetAsync(OperatorRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BurstWithTheIssuerUnreachable_DoesNotMultiplyFetches()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new ConnectorsFactory(issuer: issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests == 1, "a primeira tentativa não aconteceu");
        var token = issuer.Issue();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => factory.CreateClientWithToken(token).GetAsync(OperatorRoute)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode));
        Assert.Equal(1, issuer.DiscoveryRequests);
    }

    [Fact]
    public async Task NewKeyOutsideTheCache_IsUnauthorizedOnce_ThenAccepted()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory(issuer: issuer);
        await factory.WaitForIssuerKeysAsync();
        var newKey = TestIssuer.CreateKey();
        issuer.Publish(newKey);
        var client = factory.CreateClientWithToken(issuer.Issue(key: newKey));
        var jwksBefore = issuer.JwksRequests;

        var first = await client.GetAsync(OperatorRoute);
        await Poll.UntilAsync(async () => (await client.GetAsync(OperatorRoute)).StatusCode == HttpStatusCode.OK, "a chave nova não foi aceita");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(jwksBefore + 1, issuer.JwksRequests);
    }

    [Fact]
    public async Task InventedKids_DoNotBecomeAFetchBurst()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory(issuer: issuer);
        await factory.WaitForIssuerKeysAsync();
        var jwksBefore = issuer.JwksRequests;

        var first = await factory.CreateClientWithToken(issuer.Issue(key: TestIssuer.CreateKey())).GetAsync(OperatorRoute);
        await Poll.UntilAsync(() => issuer.JwksRequests == jwksBefore + 1, "o kid desconhecido não disparou busca");
        var second = await factory.CreateClientWithToken(issuer.Issue(key: TestIssuer.CreateKey())).GetAsync(OperatorRoute);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(jwksBefore + 1, issuer.JwksRequests);
    }

    // Sem o UseLastKnownGoodConfiguration = false, a configuração anterior validaria a chave
    // retirada por até 1 h.
    [Fact]
    public async Task RotatedKey_BothValidWhilePublished_OldOneRefusedAfterRetired()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory(issuer: issuer, issuerKeysSchedule: new IssuerKeysSchedule
        {
            UnknownKidRefreshInterval = TimeSpan.FromSeconds(1),
        });
        await factory.WaitForIssuerKeysAsync();
        var oldClient = factory.CreateClientWithToken(issuer.Issue());
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync(OperatorRoute)).StatusCode);

        var newKey = TestIssuer.CreateKey();
        issuer.Publish(newKey);
        var newClient = factory.CreateClientWithToken(issuer.Issue(key: newKey));
        await Poll.UntilAsync(async () => (await newClient.GetAsync(OperatorRoute)).StatusCode == HttpStatusCode.OK, "a chave nova não foi aceita");
        Assert.Equal(HttpStatusCode.OK, (await oldClient.GetAsync(OperatorRoute)).StatusCode);

        issuer.Retire(issuer.SigningKey);
        var jwksBefore = issuer.JwksRequests;
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        RequestRefresh(factory);
        await Poll.UntilAsync(() => issuer.JwksRequests > jwksBefore, "a renovação pedida não aconteceu");
        await Poll.UntilAsync(async () => (await oldClient.GetAsync(OperatorRoute)).StatusCode == HttpStatusCode.Unauthorized, "a chave retirada continuou valendo");

        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync(OperatorRoute)).StatusCode);
    }

    [Fact]
    public async Task DivergentIssuerInDiscovery_KeepsTheKeysOut_AndLogsBothValues()
    {
        const string announced = "https://outro.exemplo.test/";
        var issuer = new TestIssuer { AnnouncedIssuer = announced };
        await using var factory = new ConnectorsFactory(issuer: issuer);
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests >= 1, "a primeira tentativa não aconteceu");

        var response = await factory.CreateClientWithToken(issuer.Issue()).GetAsync(OperatorRoute);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, issuer.JwksRequests);
        Assert.Contains(factory.Logs.Lines, line =>
            line.StartsWith("Error", StringComparison.Ordinal)
            && line.Contains(announced, StringComparison.Ordinal)
            && line.Contains(TestIssuer.Issuer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeysAreFetchedOnlyFromTheInternalAddress()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory(issuer: issuer);
        await factory.WaitForIssuerKeysAsync();

        Assert.Equal(
            [
                new Uri(TestIssuer.InternalBaseUrl + IssuerKeysRetriever.DiscoveryPath),
                new Uri(TestIssuer.InternalBaseUrl + IssuerKeysRetriever.JwksPath),
            ],
            issuer.Requests);
    }

    private static void RequestRefresh(ConnectorsFactory factory) =>
        factory.Services.GetRequiredService<IConfigurationManager<OpenIdConnectConfiguration>>().RequestRefresh();
}
