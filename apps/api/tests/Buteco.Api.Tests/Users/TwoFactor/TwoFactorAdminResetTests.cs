using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// "Redefinir verificação" pelo admin (spec <c>two-factor-totp</c>, "Redefinição da
/// verificação pelo admin"; design.md da change verificacao-2-etapas-totp, D9). Um caso por
/// cenário.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 10.2: sem a troca do
/// stamp, <see cref="ResetAnotherUser_TurnsItOff_AndTheirRenewalIsRefused"/> reprova; sem a
/// recusa de si mesmo, <see cref="ResetYourself_Is403"/> reprova.
/// </para>
///
/// <para>
/// A redefinição revoga as autorizações (design.md da change rotas-conta-usuarios, D2), e o
/// registro está no tasks.md dela, 4.2: sem a revogação,
/// <see cref="ResetAnotherUser_EndsTheirSessionsInTheList"/> reprova.
/// </para>
/// </summary>
public class TwoFactorAdminResetTests(TwoFactorAdminResetTests.Fixture factory) : IClassFixture<TwoFactorAdminResetTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client() => TwoFactorAccount.Anonymous(factory);

    private async Task<IReadOnlyList<UserSessions.Session>> LiveAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserSessions>().ListLiveAsync(userId, CancellationToken.None);
    }

    [Fact]
    public async Task ResetAnotherUser_TurnsItOff_AndTheirRenewalIsRefused()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "admin");
        var other = await TwoFactorAccount.CreateAsync(factory, "outro");
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, other.User.Id);
        var client = Client();

        var response = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, $"/users/{other.User.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var state = await TwoFactorTestClient.StateAsync(factory.Services, other.User.Id);
        Assert.False(state.Enabled);
        Assert.Equal(0, state.Codes);
        var (login, _) = await TwoFactorTestClient.PasswordStepAsync(client, other.User.Email, TwoFactorAccount.Password);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var renewal = await IdentityIssuerUserFlow.RefreshAsync(client, other.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, renewal.StatusCode);
        Assert.Contains("invalid_grant", await renewal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetAnotherUser_EndsTheirSessionsInTheList()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "admin-lista");
        var other = await TwoFactorAccount.CreateAsync(factory, "outro-lista");
        await IdentityIssuerUserFlow.SignInAsync(Client(), factory.Services, other.User.Email, TwoFactorAccount.Password);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, other.User.Id);
        Assert.Equal(2, (await LiveAsync(other.User.Id)).Count);

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{other.User.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await LiveAsync(other.User.Id));
        Assert.All(await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, other.User.Id), status => Assert.Equal("revoked", status));
    }

    [Fact]
    public async Task ResetYourself_Is403()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "a-si-mesmo");
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, admin.User.Id);

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{admin.User.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True((await TwoFactorTestClient.StateAsync(factory.Services, admin.User.Id)).Enabled);
    }

    [Fact]
    public async Task ResetSomeoneWithoutTheVerification_Is409_AndTheStampStays()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "admin-409");
        var other = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("sem"), TwoFactorAccount.Password);
        var before = (await TwoFactorTestClient.StateAsync(factory.Services, other.Id)).Stamp;

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{other.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, (await TwoFactorTestClient.StateAsync(factory.Services, other.Id)).Stamp);
    }

    [Fact]
    public async Task ResetSomeoneWithoutTheVerification_KeepsTheirSessions()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "admin-409-sessoes");
        var other = await TwoFactorAccount.CreateAsync(factory, "sem-sessoes");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{other.User.Id}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await LiveAsync(other.User.Id));
    }

    [Fact]
    public async Task ResetAnUnknownUser_Is404()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "admin-404");

        var response = await TwoFactorTestClient.SendAsync(Client(), HttpMethod.Post, $"/users/{Guid.NewGuid()}/two-factor/reset", admin.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
