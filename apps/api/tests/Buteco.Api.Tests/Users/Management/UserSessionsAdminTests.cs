using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Encerrar todas as sessões de outro usuário" (spec <c>user-management</c>; design.md da
/// change rotas-conta-usuarios, D4).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.9: com
/// <c>"Status" = 'valid'</c> retirado da revogação, <see cref="EndSomeoneElsesSessions"/>
/// reprova (a autorização já revogada entra na contagem).
/// </para>
/// </summary>
public class UserSessionsAdminTests(UserSessionsAdminTests.Fixture factory) : IClassFixture<UserSessionsAdminTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    [Fact]
    public async Task EndSomeoneElsesSessions()
    {
        var caller = await SignedInUser.CreateAsync(factory, "encerra-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "encerra-alvo");
        var second = await target.SignInAgainAsync(factory);
        var alreadyEnded = await target.SignInAgainAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent, (await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post,
            $"/account/sessions/{alreadyEnded.AuthorizationId}/revoke", alreadyEnded.AccessToken)).StatusCode);

        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{target.User.Id}/sessions/revoke", caller.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await TwoFactorTestClient.JsonAsync(response)).GetProperty("encerradas").GetInt32());
        Assert.False(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
        Assert.False(await SignedInUser.RenewsAsync(factory, second.RefreshToken));
        var fresh = await target.SignInAgainAsync(factory);
        Assert.True(await SignedInUser.RenewsAsync(factory, fresh.RefreshToken));
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "encerra-404");

        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{Guid.NewGuid()}/sessions/revoke", caller.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
