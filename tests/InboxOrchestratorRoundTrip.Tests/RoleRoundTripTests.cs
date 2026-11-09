extern alias ApiAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// O papel com o emissor real (spec <c>route-roles</c>, "Token do painel sem papel recebe 401 sem
/// corpo"; design.md da change papeis-no-token, D3, Q12; convenção 11).
/// <list type="bullet">
/// <item>o Admin do bootstrap entra com <c>role</c> <c>admin</c> no access token;</item>
/// <item>o access token emitido antes do deploy não tem <c>role</c>. Um desses, assinado pelo anel real
/// para a sessão de verdade, recebe o <c>401</c> sem corpo no <c>apps/api</c> e no <c>apps/inbox</c>, e
/// o painel renova: o token da renovação traz o papel gravado, e a repetição passa.</item>
/// </list>
/// </summary>
public class RoleRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    [Fact]
    public async Task ATokenWithoutRole_Is401WithoutBody_OnBothApps_AndTheRenewalBringsTheRole()
    {
        var api = fixture.ApiFactory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        var session = await IssuerUserFlow.TokensAsync(
            RoundTripFixture.IssuerDatabaseOf(fixture.ApiFactory), api, WarmUpAsync, RoundTripFixture.ApiPublicBaseUrl,
            RoundTripFixture.OperatorPassword, "openid offline_access buteco-api buteco-inbox");
        var real = new JsonWebToken(session.AccessToken);
        Assert.Equal("admin", real.GetPayloadValue<string>("role"));

        var withoutRole = await IssueWithoutRoleAsync(real.Subject);

        var apiBefore = await GetAsync(fixture.ApiFactory.CreateClient(), "/agents", withoutRole);
        var inboxBefore = await PollAsync(() => GetAsync(fixture.InboxFactory.CreateClient(), "/channels", withoutRole), response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        await AssertTokenRejectionAsync(apiBefore);
        await AssertTokenRejectionAsync(inboxBefore);

        var renewed = await IssuerUserFlow.RefreshAsync(api, session.RefreshToken!);
        Assert.Equal("admin", new JsonWebToken(renewed.AccessToken).GetPayloadValue<string>("role"));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(fixture.ApiFactory.CreateClient(), "/agents", renewed.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(fixture.InboxFactory.CreateClient(), "/channels", renewed.AccessToken)).StatusCode);
    }

    private static async Task AssertTokenRejectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }

    // Um access token do buteco-painel como os de antes da change: os claims do fluxo real, sem role,
    // gerado e assinado pelo próprio OpenIddict do apps/api, com as duas audiências.
    private async Task<string> IssueWithoutRoleAsync(string subject)
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictServerFactory>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOpenIddictServerDispatcher>();
        var transaction = await factory.CreateTransactionAsync();
        var now = DateTimeOffset.UtcNow;

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, subject).SetClaim(Claims.ClientId, "buteco-painel");
        var principal = new ClaimsPrincipal(identity)
            .SetCreationDate(now)
            .SetExpirationDate(now.AddMinutes(15))
            .SetAudiences("buteco-api", "buteco-inbox")
            .SetScopes("openid", "buteco-api", "buteco-inbox")
            .SetPresenters("buteco-painel")
            .SetClaim(Claims.Private.Issuer, transaction.Options.Issuer!.AbsoluteUri);

        var context = new GenerateTokenContext(transaction)
        {
            ClientId = "buteco-painel",
            CreateTokenEntry = false,
            IsReferenceToken = false,
            PersistTokenPayload = false,
            Principal = principal,
            TokenFormat = TokenFormats.Private.JsonWebToken,
            TokenType = TokenTypeIdentifiers.AccessToken,
        };
        await dispatcher.DispatchAsync(context);
        return context.IsRejected || string.IsNullOrEmpty(context.Token)
            ? throw new InvalidOperationException($"O OpenIddict recusou o token sem papel: {context.Error} {context.ErrorDescription}")
            : context.Token;
    }

    private async Task WarmUpAsync()
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Users.Bootstrap.IUserBootstrapper>().RunAsync(CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await send();
            if (done(response) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(200);
        }
    }
}
