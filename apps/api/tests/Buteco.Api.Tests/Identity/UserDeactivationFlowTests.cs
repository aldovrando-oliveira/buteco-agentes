using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Todo token novo de usuário confere estado e stamp (spec <c>issuer-user-login</c>,
/// "Refresh rotativo e deslizante de 14 dias; todo token novo relê o usuário" e "Cookie de
/// usuário desativado depois do login é recusado"; design.md da change
/// usuarios-locais-login-emissor, D4). A parte do login está em <c>Users/UserDeactivationTests</c>.
/// A reativação é feita no banco, só com <c>status</c>: a rota é da #219, e reativar não
/// devolve o stamp antigo.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.8:
/// <list type="bullet">
/// <item>sem a releitura do usuário no handler, <see cref="DeactivatedUser_CannotRefresh"/> reprova;</item>
/// <item>sem a comparação do stamp, <see cref="DeactivatedAndReactivated_CannotRefreshWithTheOldToken"/> reprova;</item>
/// <item>com o handler só no <c>refresh_token</c>, <see cref="DeactivatedBetweenAuthorizeAndRedeem_GetsNoTokens"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class UserDeactivationFlowTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<(Buteco.Api.Users.User User, string Email)> UserAsync()
    {
        var email = LocalUserSeed.UniqueEmail("fluxo-desativado");
        return (await LocalUserSeed.CreateUserAsync(factory.Services, email, Password), email);
    }

    private async Task ReactivateAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE users SET status = 'active', deactivated_at = NULL WHERE id = {userId}");
    }

    private static async Task AssertInvalidGrantAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(Errors.InvalidGrant, body);
        Assert.DoesNotContain(Parameters.AccessToken, body);
    }

    [Fact]
    public async Task DeactivatedUser_CannotRefresh()
    {
        var (user, email) = await UserAsync();
        var tokens = await IdentityIssuerUserFlow.SignInAsync(Client(), factory.Services, email, Password);

        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);

        await AssertInvalidGrantAsync(await IdentityIssuerUserFlow.RefreshAsync(Client(), tokens.RefreshToken));
    }

    [Fact]
    public async Task DeactivatedAndReactivated_CannotRefreshWithTheOldToken()
    {
        var (user, email) = await UserAsync();
        var tokens = await IdentityIssuerUserFlow.SignInAsync(Client(), factory.Services, email, Password);

        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);
        await ReactivateAsync(user.Id);

        await AssertInvalidGrantAsync(await IdentityIssuerUserFlow.RefreshAsync(Client(), tokens.RefreshToken));
    }

    [Fact]
    public async Task DeactivatedBetweenAuthorizeAndRedeem_GetsNoTokens()
    {
        var (user, email) = await UserAsync();
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var client = Client();
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, email, Password);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var code = IdentityIssuerUserFlow.CodeFrom(
            await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce), cookie));

        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);

        await AssertInvalidGrantAsync(await IdentityIssuerUserFlow.RedeemAsync(client, code, pkce.Verifier));
    }

    [Fact]
    public async Task CookieInFlight_OfADeactivatedUser_IsRefusedAtTheAuthorize()
    {
        var (user, email) = await UserAsync();
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(Client(), email, Password);

        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);
        var response = await IdentityIssuerUserFlow.AuthorizeAsync(
            Client(), IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/entrar?retorno=", response.Headers.Location!.OriginalString);
    }
}
