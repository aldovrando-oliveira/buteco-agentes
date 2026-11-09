using System.Security.Claims;
using System.Text.Json;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// "O papel no access token e no id token" (spec <c>issuer-user-login</c>; design.md da change
/// papeis-no-token, D3): o authorize põe o <c>role</c> gravado nos dois tokens, e a troca do código
/// e cada renovação o trocam pelo gravado naquele momento.
/// </summary>
public class UserGrantRoleClaimTests(UserGrantRoleClaimTests.Fixture factory) : IClassFixture<UserGrantRoleClaimTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(part.PadRight(part.Length + (4 - part.Length % 4) % 4, '='))).RootElement;
    }

    private static string? RoleOf(string jwt) =>
        Payload(jwt).TryGetProperty("role", out var role) ? role.GetString() : null;

    private async Task<IdentityIssuerUserFlow.Tokens> RenewAsync(string refreshToken) =>
        await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(SignedInUser.Anonymous(factory), refreshToken));

    [Fact]
    public async Task TheLogin_PutsTheRole_InTheAccessAndIdToken()
    {
        var me = await SignedInUser.CreateAsync(factory, "papel-login", role: UserRole.Operator);

        Assert.Equal("operator", RoleOf(me.AccessToken));
        Assert.Equal("operator", RoleOf(me.Tokens.IdToken));
    }

    [Fact]
    public async Task AChangedRole_ReachesTheNextRenewal_AndTheOneAfter()
    {
        var me = await SignedInUser.CreateAsync(factory, "papel-renova");
        Assert.Equal("admin", RoleOf(me.AccessToken));

        await LocalUserSeed.SetRoleAsync(factory.Services, me.User.Id, UserRole.Reader);
        var renewed = await RenewAsync(me.RefreshToken);
        var again = await RenewAsync(renewed.RefreshToken);

        Assert.Equal("reader", RoleOf(renewed.AccessToken));
        Assert.Equal("reader", RoleOf(renewed.IdToken));
        Assert.Equal("reader", RoleOf(again.AccessToken));
        Assert.Equal("reader", RoleOf(again.IdToken));
    }

    [Fact]
    public async Task TheRole_IsAClaimApart_AndTheSubStaysTheUserId()
    {
        var me = await SignedInUser.CreateAsync(factory, "papel-sub");

        var access = Payload(me.AccessToken);
        Assert.Equal(me.User.Id.ToString(), access.GetProperty("sub").GetString());
        Assert.Equal("admin", access.GetProperty("role").GetString());
    }

    /// <summary>
    /// O refresh emitido antes da change não tem <c>role</c> no principal. Não há como emitir um pelo
    /// fluxo de hoje, então o handler recebe o principal sem o claim, como o OpenIddict o restaura de
    /// um refresh desses, e o caso afirma o que ele grava antes da emissão.
    /// </summary>
    [Fact]
    public async Task ARefreshPrincipalWithoutRole_GetsTheRecordedRole_WithBothDestinations()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("papel-antigo"), SignedInUser.DefaultPassword, role: UserRole.Operator);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        var stored = await SignedInUser.ReloadAsync(factory.Services, user.Id);

        var identity = new ClaimsIdentity("test", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString()).SetClaim(IssuerSession.SecurityStampClaim, stored.SecurityStamp);
        var principal = new ClaimsPrincipal(identity);

        var context = new HandleTokenRequestContext(new OpenIddictServerTransaction
        {
            Options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue,
            Logger = NullLogger.Instance,
            Request = new OpenIddictRequest { GrantType = GrantTypes.RefreshToken },
        })
        {
            Principal = principal,
        };

        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new UserGrantTokenHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>(), TimeProvider.System);
        await handler.HandleAsync(context);

        Assert.False(context.IsRejected);
        var role = principal.FindFirst(Claims.Role);
        Assert.NotNull(role);
        Assert.Equal("operator", role.Value);
        Assert.Equal([Destinations.AccessToken, Destinations.IdentityToken], role.GetDestinations().Order(StringComparer.Ordinal));
    }
}
