using System.Net;
using System.Text.Json;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.AccessLinks;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// O convidado na lista e no detalhe (spec <c>user-management</c>; design.md da change
/// convites-redefinicao-senha, D5 e D10). Host próprio: o caso do vencido avança o relógio.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 4.2): <c>vencido</c> calculado sem o relógio
/// (<c>expiraEm &lt; criadoEm + validade</c>) → <see cref="AnExpiredInvitation_StaysListed_AsExpired"/>
/// reprova.
/// </para>
/// </summary>
public class UserListInvitationTests(UserListInvitationTests.Fixture factory) : IClassFixture<UserListInvitationTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private async Task<JsonElement> RowAsync(SignedInUser caller, Guid id)
    {
        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/users", caller.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await TwoFactorTestClient.JsonAsync(response)).EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
    }

    private async Task<JsonElement> DetailAsync(SignedInUser caller, Guid id)
    {
        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, $"/users/{id}", caller.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TwoFactorTestClient.JsonAsync(response);
    }

    [Fact]
    public async Task APendingInvitation_IsListed_AsInvited_WithoutSignInForms()
    {
        var caller = await SignedInUser.CreateAsync(factory, "lista-convite");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var row = await RowAsync(caller, invited.User.Id);

        Assert.Equal("convidado", row.GetProperty("estado").GetString());
        Assert.Empty(row.GetProperty("formasDeEntrada").EnumerateArray());
        Assert.False(row.GetProperty("duasEtapas").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("ultimoAcesso").ValueKind);
        var invitation = row.GetProperty("convite");
        Assert.Equal(invited.User.CreatedAt + AccessLinkToken.InvitationLifetime, invitation.GetProperty("expiraEm").GetDateTimeOffset());
        Assert.False(invitation.GetProperty("vencido").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await RowAsync(caller, caller.User.Id)).GetProperty("convite").ValueKind);
    }

    [Fact]
    public async Task TheDetail_NamesWhoInvited_AndIsNullForWhoWasNotInvited()
    {
        var caller = await SignedInUser.CreateAsync(factory, "detalhe-convite", name: "Quem Convidou");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var detail = await DetailAsync(caller, invited.User.Id);

        var invitedBy = detail.GetProperty("convidadoPor");
        Assert.Equal(caller.User.Id, invitedBy.GetProperty("id").GetGuid());
        Assert.Equal("Quem Convidou", invitedBy.GetProperty("nome").GetString());
        Assert.Equal(invited.User.CreatedAt, detail.GetProperty("criadoEm").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(caller, caller.User.Id)).GetProperty("convidadoPor").ValueKind);
    }

    [Fact]
    public async Task AnExpiredInvitation_StaysListed_AsExpired()
    {
        var caller = await SignedInUser.CreateAsync(factory, "lista-vencido");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        factory.Clock.Advance(AccessLinkToken.InvitationLifetime + TimeSpan.FromMinutes(1));
        var renewed = await SignedInUser.SignInAsync(factory, caller.User, caller.Password);
        var row = await RowAsync(renewed, invited.User.Id);

        Assert.Equal("convidado", row.GetProperty("estado").GetString());
        Assert.True(row.GetProperty("convite").GetProperty("vencido").GetBoolean());
    }
}
