using System.Net;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// "A renovação leva o nome e o e-mail vigentes" (spec <c>issuer-user-login</c>; design.md da
/// change rotas-conta-usuarios, D5): o painel lê os dois do id token, e o principal da renovação
/// é o do login.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 7.2: com a troca dos
/// claims retirada, <see cref="AnEditedName_ReachesTheNextRenewal_AndTheOneAfter"/> reprova. A
/// perda do destino do <c>SetClaim</c> (o claim some do id token) é coberta pelo mesmo caso.
/// </para>
/// </summary>
public class UserGrantClaimsRefreshTests(UserGrantClaimsRefreshTests.Fixture factory) : IClassFixture<UserGrantClaimsRefreshTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(part.PadRight(part.Length + (4 - part.Length % 4) % 4, '='))).RootElement;
    }

    private async Task<IdentityIssuerUserFlow.Tokens> RenewAsync(string refreshToken) =>
        await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(SignedInUser.Anonymous(factory), refreshToken));

    private Task<HttpResponseMessage> EditAsync(SignedInUser caller, Guid id, string name, string email) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Put, $"/users/{id}", caller.AccessToken, new { nome = name, email });

    [Fact]
    public async Task AnEditedName_ReachesTheNextRenewal_AndTheOneAfter()
    {
        var me = await SignedInUser.CreateAsync(factory, "renomeia", name: "Nome Antigo");
        Assert.Equal("Nome Antigo", Payload(me.Tokens.IdToken).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(me, me.User.Id, "Nome Novo", me.User.Email)).StatusCode);

        var renewed = await RenewAsync(me.RefreshToken);
        var again = await RenewAsync(renewed.RefreshToken);

        Assert.Equal("Nome Novo", Payload(renewed.IdToken).GetProperty("name").GetString());
        Assert.Equal("Nome Novo", Payload(again.IdToken).GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnEditedEmail_ReachesTheNextRenewal()
    {
        var me = await SignedInUser.CreateAsync(factory, "troca-email");
        var newEmail = LocalUserSeed.UniqueEmail("email-novo");
        Assert.Equal(HttpStatusCode.OK, (await EditAsync(me, me.User.Id, "Pessoa de Teste", newEmail)).StatusCode);

        var renewed = await RenewAsync(me.RefreshToken);

        Assert.Equal(newEmail, Payload(renewed.IdToken).GetProperty("email").GetString());
    }

    [Fact]
    public async Task TheAccessToken_StillHasNeitherNameNorEmail()
    {
        var me = await SignedInUser.CreateAsync(factory, "access-sem");
        await EditAsync(me, me.User.Id, "Outro Nome", me.User.Email);

        var access = Payload((await RenewAsync(me.RefreshToken)).AccessToken);

        Assert.False(access.TryGetProperty("name", out _));
        Assert.False(access.TryGetProperty("email", out _));
    }
}
