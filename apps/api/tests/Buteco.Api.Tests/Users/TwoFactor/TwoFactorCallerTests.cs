using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// As rotas de 2 etapas atendem só o usuário real do token (spec <c>two-factor-totp</c>, "As
/// rotas de 2 etapas atendem só o usuário real do token"; design.md da change
/// verificacao-2-etapas-totp, D8 e D9, revisão do dono de 01/11/2026).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.1: com a redefinição
/// aceitando o HMAC (sem o <c>CurrentUser</c>), <see cref="OperatorHmac_OnTheReset_Is403_AndChangesNothing"/>
/// reprova.
/// </para>
/// </summary>
public class TwoFactorCallerTests(TwoFactorCallerTests.Fixture factory) : IClassFixture<TwoFactorCallerTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient HmacClient()
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        TestAuthentication.AttachOperatorToken(client, factory.Services);
        return client;
    }

    [Fact]
    public async Task OperatorHmac_OnMyAccount_Is403()
    {
        var response = await HmacClient().GetAsync("/account/two-factor");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OperatorHmac_OnTheReset_Is403_AndChangesNothing()
    {
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo"), TwoFactorAccount.Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, target.Id);
        var before = await TwoFactorTestClient.StateAsync(factory.Services, target.Id);

        var response = await HmacClient().PostAsync($"/users/{target.Id}/two-factor/reset", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await TwoFactorTestClient.StateAsync(factory.Services, target.Id));
    }

    [Fact]
    public async Task DeactivatedUser_WithAStillValidAccessToken_Is403OnBoth()
    {
        var account = await TwoFactorAccount.CreateAsync(factory, "desativado");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo-desativado"), TwoFactorAccount.Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, target.Id);
        await LocalUserSeed.DeactivateAsync(factory.Services, account.User.Id);
        var client = TwoFactorAccount.Anonymous(factory);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await TwoFactorTestClient.SendAsync(client, HttpMethod.Get, "/account/two-factor", account.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, $"/users/{target.Id}/two-factor/reset", account.AccessToken)).StatusCode);
        Assert.True((await TwoFactorTestClient.StateAsync(factory.Services, target.Id)).Enabled);
    }
}
