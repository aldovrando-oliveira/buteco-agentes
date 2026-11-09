using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// "Prazos da mudança de papel" (spec <c>route-roles</c>; design.md da change papeis-no-token, D3,
/// Q1): fora das rotas de Admin, o papel é o do token, e muda na renovação seguinte. O prazo do
/// resto é o do access token já emitido (até 15 min 30 s), o mesmo aceito na M1 depois da
/// desativação. Mudar o papel não troca o stamp: a sessão continua renovando.
/// </summary>
public class PanelRoleDeadlinesTests(PanelRoleDeadlinesTests.Fixture factory) : IClassFixture<PanelRoleDeadlinesTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> CreateAgentAsync(string accessToken) =>
        TwoFactorTestClient.SendAsync(
            SignedInUser.Anonymous(factory), HttpMethod.Post, "/agents", accessToken,
            new { name = "Agente do prazo", instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });

    private async Task<string> RenewAsync(string refreshToken) =>
        (await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(SignedInUser.Anonymous(factory), refreshToken))).AccessToken;

    [Fact]
    public async Task UntilTheRenewal_ADemotedOperator_StillWrites_AndAfterItDoesNot()
    {
        var me = await SignedInUser.CreateAsync(factory, "prazo-rebaixa", role: UserRole.Operator);
        await LocalUserSeed.SetRoleAsync(factory.Services, me.User.Id, UserRole.Reader);

        var before = await CreateAgentAsync(me.AccessToken);
        var after = await CreateAgentAsync(await RenewAsync(me.RefreshToken));

        Assert.NotEqual(HttpStatusCode.Unauthorized, before.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }

    [Fact]
    public async Task APromotion_ArrivesOnTheNextRenewal()
    {
        var me = await SignedInUser.CreateAsync(factory, "prazo-promove", role: UserRole.Reader);
        await LocalUserSeed.SetRoleAsync(factory.Services, me.User.Id, UserRole.Operator);

        var before = await CreateAgentAsync(me.AccessToken);
        var after = await CreateAgentAsync(await RenewAsync(me.RefreshToken));

        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, after.StatusCode);
    }
}
