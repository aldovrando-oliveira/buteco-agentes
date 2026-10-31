using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O encerramento de sessão revoga a autorização e sempre termina (spec
/// <c>issuer-user-login</c>; design.md da change usuarios-locais-login-emissor, D3.9 e 2.4 (b)).
/// </summary>
public class EndSessionTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<IdentityIssuerUserFlow.Tokens> SignInAsync()
    {
        var email = LocalUserSeed.UniqueEmail("logout");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        return await IdentityIssuerUserFlow.SignInAsync(Client(), factory.Services, email, Password);
    }

    private static string LogoutUrl(string? hint, string? postLogout) =>
        QueryHelpers.AddQueryString("/connect/logout", new Dictionary<string, string?>
        {
            [Parameters.IdTokenHint] = hint,
            [Parameters.PostLogoutRedirectUri] = postLogout,
        }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));

    private static void AssertCookieCleared(HttpResponseMessage response)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("buteco-sessao=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidHint_RevokesTheSession_ClearsTheCookie_AndRedirectsToTheRegisteredAddress()
    {
        var tokens = await SignInAsync();

        var response = await Client().GetAsync(LogoutUrl(tokens.IdToken, IdentityIssuerUserFlow.PostLogoutRedirectUri));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(IdentityIssuerUserFlow.PostLogoutRedirectUri, response.Headers.Location!.AbsoluteUri);
        AssertCookieCleared(response);

        var refresh = await IdentityIssuerUserFlow.RefreshAsync(Client(), tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subject = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(tokens.IdToken).Subject;
        var statuses = await db.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(a => a.Subject == subject).Select(a => a.Status).ToListAsync();
        Assert.Equal([Statuses.Revoked], statuses);
    }

    [Fact]
    public async Task ValidHint_WithoutAddress_GoesToTheLoginScreen()
    {
        var tokens = await SignInAsync();

        var response = await Client().GetAsync(LogoutUrl(tokens.IdToken, postLogout: null));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task InvalidHint_StillClearsTheCookie_AndGoesToTheLoginScreen()
    {
        var tokens = await SignInAsync();
        var tampered = tokens.IdToken[..^4] + (tokens.IdToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");

        var response = await Client().GetAsync(LogoutUrl(tampered, IdentityIssuerUserFlow.PostLogoutRedirectUri));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", response.Headers.Location!.OriginalString);
        AssertCookieCleared(response);
    }

    [Fact]
    public async Task UnregisteredAddress_IsNotFollowed()
    {
        var tokens = await SignInAsync();

        var response = await Client().GetAsync(LogoutUrl(tokens.IdToken, "https://atacante.exemplo/saiu"));

        Assert.DoesNotContain("atacante", response.Headers.Location?.OriginalString ?? "");
        Assert.Equal("/entrar", response.Headers.Location!.OriginalString);
    }
}
