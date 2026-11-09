using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O <c>papel</c> nas respostas (spec <c>user-management</c>, "O papel na lista e no detalhe", e
/// <c>user-account</c>, "Os próprios dados"; design.md da change papeis-no-token, D3): no JSON,
/// <c>admin</c>, <c>operador</c> e <c>leitor</c>, e sempre o gravado, nunca o do token.
/// </summary>
public class UserRoleResponsesTests(UserRoleResponsesTests.Fixture factory) : IClassFixture<UserRoleResponsesTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client => SignedInUser.Anonymous(factory);

    [Fact]
    public async Task TheList_AndTheDetail_CarryEachUsersRole()
    {
        var admin = await SignedInUser.CreateAsync(factory, "resposta-admin");
        var operatorUser = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("resposta-operador"), SignedInUser.DefaultPassword, role: UserRole.Operator);
        var invited = await AccessLinkClient.InviteAsync(Client, admin, "Leitora Convidada", LocalUserSeed.UniqueEmail("resposta-leitor"), admin.Password, "leitor");
        var invitedId = await AccessLinkClient.InvitedIdAsync(invited);

        var list = await TwoFactorTestClient.JsonAsync(await TwoFactorTestClient.SendAsync(Client, HttpMethod.Get, "/users", admin.AccessToken));
        var detail = await TwoFactorTestClient.JsonAsync(await TwoFactorTestClient.SendAsync(Client, HttpMethod.Get, $"/users/{operatorUser.Id}", admin.AccessToken));

        string RoleIn(Guid id) => list.EnumerateArray().Single(user => user.GetProperty("id").GetGuid() == id).GetProperty("papel").GetString()!;
        Assert.Equal("admin", RoleIn(admin.User.Id));
        Assert.Equal("operador", RoleIn(operatorUser.Id));
        Assert.Equal("leitor", RoleIn(invitedId));
        Assert.Equal("operador", detail.GetProperty("papel").GetString());
    }

    [Fact]
    public async Task Account_CarriesTheRecordedRole_NotTheTokens()
    {
        var me = await SignedInUser.CreateAsync(factory, "resposta-conta");
        await LocalUserSeed.SetRoleAsync(factory.Services, me.User.Id, UserRole.Reader);

        var response = await TwoFactorTestClient.SendAsync(Client, HttpMethod.Get, "/account", me.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("leitor", (await TwoFactorTestClient.JsonAsync(response)).GetProperty("papel").GetString());
    }
}
