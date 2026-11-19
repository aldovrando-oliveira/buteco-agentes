using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O caso 2 da #281 (change conectar-falha-minha-conta, design D2, decisão do dono de 17/11/2026, a N): quando o
/// cliente OIDC recusa o state ANTES de restaurar as propriedades do Buteco (ausente, forjado, vencido ou já usado), a
/// intenção é desconhecida, e a falha vai ao <c>/entrar?recusa=falha</c>, sem <c>provedor</c>, mesmo que o fluxo
/// fosse um conectar. Nenhum outro dado (o cookie do fluxo, a sessão) escolhe o destino. Os quatro partem de um
/// conectar começado, com o cookie do fluxo dele no pedido.
/// </summary>
public class FederatedUnreadableStateTests(RestartedWithoutProviderFixture fixture) : IClassFixture<RestartedWithoutProviderFixture>
{
    [Fact]
    public async Task TheReturn_WithoutState_IsFailure_ToTheLogin()
    {
        var person = await SignedInUser.CreateAsync(fixture, "sem-state");
        var started = await UnreadableState.StartConnectAsync(fixture, person);
        var restarted = await fixture.WithoutProviderAsync();

        // Com provedor e sem nenhum (o mesmo apps/api depois do reinício): os dois ao login.
        foreach (var host in new LocalUsersFixture[] { fixture, restarted })
        {
            var response = await UnreadableState.ReturnAsync(host, new() { ["code"] = "codigo" }, started.FlowCookie);
            UnreadableState.AssertToTheLogin(response);
        }

        Assert.Empty(await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id));
    }

    [Fact]
    public async Task TheReturn_WithAForgedState_IsFailure_ToTheLogin()
    {
        var person = await SignedInUser.CreateAsync(fixture, "state-forjado");
        var started = await UnreadableState.StartConnectAsync(fixture, person);
        var restarted = await fixture.WithoutProviderAsync();

        foreach (var host in new LocalUsersFixture[] { fixture, restarted })
        {
            // Um reference id com o mesmo formato do Buteco (256 bytes em base64url), que ele não emitiu.
            var forged = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(256));
            var response = await UnreadableState.ReturnAsync(host, new() { ["code"] = "codigo", ["state"] = forged }, started.FlowCookie);
            UnreadableState.AssertToTheLogin(response);
        }

        Assert.Empty(await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id));
    }

    [Fact]
    public async Task TheReturn_WithAReusedConnectState_IsFailure_ToTheLogin()
    {
        var person = await SignedInUser.CreateAsync(fixture, "state-reusado");
        var started = await UnreadableState.StartConnectAsync(fixture, person);
        var code = GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("conta-reusada")));
        var first = await GoogleFlow.ReturnAsync(GoogleFlow.Client(fixture), code, started.Query["state"], started.FlowCookie);
        Assert.Equal("/account?conectado=corporativo", first.Location);
        var links = await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id);

        var again = await GoogleFlow.ReturnAsync(GoogleFlow.Client(fixture), code, started.Query["state"], started.FlowCookie);

        UnreadableState.AssertToTheLogin(again.Response);
        var after = await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id);
        Assert.Equal(links.Select(link => (link.Id, link.Subject)), after.Select(link => (link.Id, link.Subject)));
    }
}

/// <summary>O vencido, num host próprio: o relógio do fixture avança, e o OpenIddict confere o vencimento por ele.</summary>
public class FederatedExpiredStateTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    [Fact]
    public async Task TheReturn_WithAnExpiredConnectState_IsFailure_ToTheLogin()
    {
        var person = await SignedInUser.CreateAsync(fixture, "state-vencido");
        var started = await UnreadableState.StartConnectAsync(fixture, person);
        var code = GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("conta-vencida")));

        // O prazo do state token é de 10 min (FederatedLogin.FlowLifetime); o OpenIddict usa o TimeProvider do contêiner.
        fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        var returned = await GoogleFlow.ReturnAsync(GoogleFlow.Client(fixture), code, started.Query["state"], started.FlowCookie);

        UnreadableState.AssertToTheLogin(returned.Response);
        Assert.Empty(await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id));
    }
}

internal static class UnreadableState
{
    public static async Task<GoogleFlow.Started> StartConnectAsync(FederatedLoginFixture fixture, SignedInUser person)
    {
        var connect = new HttpRequestMessage(HttpMethod.Post, "/account/federated/corporativo/connect") { Content = JsonContent.Create(new { senha = person.Password }) };
        connect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);
        var started = GoogleFlow.Read(await GoogleFlow.Client(fixture).SendAsync(connect));
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        Assert.NotNull(started.FlowCookie);
        return started;
    }

    public static async Task<HttpResponseMessage> ReturnAsync(LocalUsersFixture host, Dictionary<string, string?> query, string? flowCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("/auth/federated/callback", query));
        if (flowCookie is not null)
        {
            request.Headers.Add("Cookie", flowCookie);
        }

        return await SignedInUser.Anonymous(host).SendAsync(request);
    }

    public static void AssertToTheLogin(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar?recusa=falha", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(GoogleFlow.SetCookiesOf(response), cookie => cookie.StartsWith("buteco-sessao=", StringComparison.Ordinal));
    }
}
