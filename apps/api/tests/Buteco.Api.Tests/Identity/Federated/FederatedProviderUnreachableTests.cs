using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Identity.Federated;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O provedor fora do alcance (spec <c>federated-login</c>, "Início do login federado"; design.md da change
/// provedores-federados-google, D15, APPLY-11, decisão do dono de 13/11/2026): o início responde 503 com
/// problem+json em até <see cref="FederatedLogin.ProviderTimeout"/>, e não o 400 em texto do OpenIddict. Guarda:
/// o 400 de volta (sem a busca prévia do discovery) reprova o caso.
/// </summary>
public class FederatedProviderUnreachableTests(UnreachableGoogleFixture fixture) : IClassFixture<UnreachableGoogleFixture>
{
    [Fact]
    public async Task TheStart_WithTheProviderDown_IsA503ProblemJson_WithoutCookie()
    {
        var started = await GoogleFlow.StartAsync(GoogleFlow.Client(fixture));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, started.Response.StatusCode);
        Assert.Equal("application/problem+json", started.Response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(FederatedChallenge.UnreachableMessage, await started.Response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
        Assert.Contains(fixture.Logs.Lines, line => line.EventId.Id == 1199 && line.Message.Contains("provedor_inalcancavel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connecting_WithTheProviderDown_IsA503ProblemJson()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta-fora-do-ar");
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/federated/corporativo/connect") { Content = JsonContent.Create(new { senha = person.Password }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);

        var response = await GoogleFlow.Client(fixture).SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TheInvitationStart_WithTheProviderDown_IsA503ProblemJson_WithoutCookie_AndTheInvitationUnchanged()
    {
        var caller = await SignedInUser.CreateAsync(fixture, "aceite-fora-do-ar");
        var invited = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);

        var started = await GoogleFlow.StartInvitationAsync(GoogleFlow.Client(fixture), invited.Token);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, started.Response.StatusCode);
        Assert.Equal("application/problem+json", started.Response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(FederatedChallenge.UnreachableMessage, await started.Response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
        var reloaded = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(Buteco.Api.Users.UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
    }
}

/// <summary>O provedor que não responde: o início desiste no prazo explícito (10 s), e não no das tentativas do cliente.</summary>
public class FederatedProviderHangingTests(HangingGoogleFixture fixture) : IClassFixture<HangingGoogleFixture>
{
    [Fact]
    public async Task TheStart_WithTheProviderSilent_GivesUpAtTheExplicitTimeout()
    {
        var watch = Stopwatch.StartNew();
        var started = await GoogleFlow.StartAsync(GoogleFlow.Client(fixture));
        watch.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, started.Response.StatusCode);
        Assert.InRange(watch.Elapsed, FederatedLogin.ProviderTimeout - TimeSpan.FromSeconds(1), FederatedLogin.ProviderTimeout + TimeSpan.FromSeconds(3));
    }
}

/// <summary>
/// A busca prévia do discovery passa pelo cache do cliente OIDC (D15, APPLY-11): o
/// <c>ConfigurationManager</c> da registração, o mesmo que o desafio lê. Dois inícios seguidos, um só
/// pedido de discovery ao provedor. Guarda: a busca sem cache, a cada início, reprova o caso.
/// </summary>
public class FederatedDiscoveryCacheTests(DiscoveryCountFixture fixture) : IClassFixture<DiscoveryCountFixture>
{
    [Fact]
    public async Task TwoStartsInARow_FetchTheDiscoveryOnce()
    {
        var client = GoogleFlow.Client(fixture);

        var first = await GoogleFlow.StartAsync(client);
        var second = await GoogleFlow.StartAsync(client);

        Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.Response.StatusCode);
        Assert.Equal(1, fixture.Google.DiscoveryRequests);
    }
}
