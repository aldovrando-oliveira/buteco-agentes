using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Account;

/// <summary>"Os próprios dados" (spec <c>user-account</c>; design.md da change rotas-conta-usuarios, D1).</summary>
public class AccountProfileTests(AccountProfileTests.Fixture factory) : IClassFixture<AccountProfileTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    [Fact]
    public async Task ReadsTheCallersOwnData()
    {
        var me = await SignedInUser.CreateAsync(factory, "perfil", name: "Ana Souza");
        await SignedInUser.CreateAsync(factory, "outra-pessoa");
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, me.User.Id);

        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/account", me.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.Equal(me.User.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Ana Souza", body.GetProperty("nome").GetString());
        Assert.Equal(me.User.Email, body.GetProperty("email").GetString());
        Assert.True(body.GetProperty("duasEtapas").GetBoolean());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, body.GetProperty("ultimoAcesso").ValueKind);
    }
}
