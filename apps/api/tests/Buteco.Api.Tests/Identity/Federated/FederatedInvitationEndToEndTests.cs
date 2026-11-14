using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// De ponta a ponta, sem semeador além do Admin (tasks.md da change aceite-convite-google, 3.4): o Admin convida
/// um Operador pela rota; o convidado aceita pelo provedor, cadastra a verificação com o cookie do retorno, e o
/// authorize e o <c>/connect/token</c> emitem o token do Buteco, com o papel do convite.
/// </summary>
public class FederatedInvitationEndToEndTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    [Fact]
    public async Task InviteByTheRoute_AcceptByTheProvider_EnrollAndGetTheButecoToken()
    {
        var admin = await SignedInUser.CreateAsync(fixture, "ponta-a-ponta-admin");
        var browser = GoogleFlow.Client(fixture);
        var email = LocalUserSeed.UniqueEmail("ponta-a-ponta-convidado");

        // 1. O convite pela rota, com a senha do Admin.
        var invite = await AccessLinkClient.InviteAsync(browser, admin, "Pessoa Convidada", email, admin.Password, papel: "operador");
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var token = await AccessLinkClient.TokenAsync(invite);
        var invitedId = await AccessLinkClient.InvitedIdAsync(invite);

        // 2. O aceite pelo provedor, com o e-mail do convite.
        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, token, GoogleFlow.NewAccount(email));
        Assert.Equal("/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito", returned.Location);

        // 3. O cadastro das 2 etapas com o cookie do retorno, e o token.
        var enrolled = await TwoFactorTestClient.EnrollAsync(browser, fixture.Services, returned.Cookie("buteco-sessao")!);
        Assert.Equal(10, enrolled.RecoveryCodes.Count);
        var tokens = await TwoFactorTestClient.TokensAsync(browser, fixture.Services, enrolled.SessionCookie);

        var byProvider = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        var byPassword = new JwtSecurityTokenHandler().ReadJwtToken(admin.AccessToken);
        Assert.Equal(byPassword.Issuer, byProvider.Issuer);
        Assert.Equal(byPassword.Audiences.Order(), byProvider.Audiences.Order());
        Assert.Equal(invitedId.ToString(), byProvider.Subject);
        Assert.Equal("buteco-painel", byProvider.Claims.Single(claim => claim.Type == "client_id").Value);
        Assert.Equal("operator", byProvider.Claims.Single(claim => claim.Type == "role").Value);
    }
}
