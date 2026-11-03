using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// "Rotas administrativas do link atendem só o usuário real do token" (spec
/// <c>user-access-links</c>; design.md da change convites-redefinicao-senha, D1). O HMAC de
/// operador não tem <c>sub</c>, e um usuário desativado com access token ainda válido não é
/// atendido.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 5.6): com o <c>CurrentUser</c> retirado de criar convite,
/// <see cref="OperatorHmac_Is403_OnTheFourRoutes_AndNothingIsWritten"/> reprova.
/// </para>
/// </summary>
public class AccessLinkCallerTests(AccessLinkCallerTests.Fixture factory) : IClassFixture<AccessLinkCallerTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    [Fact]
    public async Task OperatorHmac_Is403_OnTheFourRoutes_AndNothingIsWritten()
    {
        var inviter = await SignedInUser.CreateAsync(factory, "convidou");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, inviter.User.Id);
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo-hmac"), SignedInUser.DefaultPassword);
        var name = $"Pelo HMAC {Guid.NewGuid():N}";
        var client = SignedInUser.Anonymous(factory);
        TestAuthentication.AttachOperatorToken(client, factory.Services);
        var routes = new (string Path, object? Body)[]
        {
            ("/users/invitations", new { nome = name, email = LocalUserSeed.UniqueEmail("hmac"), senha = TestAuthentication.KnownOperatorPassword }),
            ($"/users/{invited.User.Id}/invitation/renew", new { senha = TestAuthentication.KnownOperatorPassword }),
            ($"/users/{invited.User.Id}/invitation/cancel", null),
            ($"/users/{target.Id}/password-reset", new { senha = TestAuthentication.KnownOperatorPassword }),
        };

        foreach (var (path, body) in routes)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (body is not null)
            {
                request.Content = System.Net.Http.Json.JsonContent.Create(body);
            }

            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"POST {path} → {(int)response.StatusCode}");
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Users.CountAsync(user => user.Name == name));
        Assert.Equal(invited.User.AccessLinkHash, (await SignedInUser.ReloadAsync(factory.Services, invited.User.Id)).AccessLinkHash);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, target.Id)).AccessLinkHash);
        Assert.Equal(0, await db.LoginThrottles.CountAsync());
    }

    [Fact]
    public async Task ADeactivatedUser_WithAValidAccessToken_Is403()
    {
        var me = await SignedInUser.CreateAsync(factory, "desativado-convida");
        await LocalUserSeed.DeactivateAsync(factory.Services, me.User.Id);

        var response = await AccessLinkClient.InviteAsync(SignedInUser.Anonymous(factory), me, "Nao", LocalUserSeed.UniqueEmail("nao"), me.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
