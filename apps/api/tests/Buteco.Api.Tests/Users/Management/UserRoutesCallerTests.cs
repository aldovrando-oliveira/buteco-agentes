using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Rotas de Minha conta atendem só o usuário real do token" (spec <c>user-account</c>) e "Rotas
/// de Usuários atendem só o usuário real do token" (spec <c>user-management</c>; design.md da
/// change rotas-conta-usuarios, D8). O HMAC de operador não tem <c>sub</c>, e um usuário
/// desativado com access token ainda válido não é atendido.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.10: com o
/// <c>CurrentUser</c> retirado de <c>PUT /users/{id}</c>, <see cref="OperatorHmac_Is403_OnEveryRoute_AndNothingChanges"/>
/// reprova.
/// </para>
/// </summary>
public class UserRoutesCallerTests(UserRoutesCallerTests.Fixture factory) : IClassFixture<UserRoutesCallerTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    [Fact]
    public async Task OperatorHmac_Is403_OnEveryRoute_AndNothingChanges()
    {
        var target = await SignedInUser.CreateAsync(factory, "alvo-hmac");
        var before = await SignedInUser.ReloadAsync(factory.Services, target.User.Id);
        var client = SignedInUser.Anonymous(factory);
        TestAuthentication.AttachOperatorToken(client, factory.Services);
        var id = target.User.Id;
        var routes = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/account", null),
            (HttpMethod.Post, "/account/password", new { senhaAtual = target.Password, novaSenha = "uma-senha-nova-longa" }),
            (HttpMethod.Get, "/account/sessions", null),
            (HttpMethod.Post, $"/account/sessions/{target.AuthorizationId}/revoke", null),
            (HttpMethod.Post, "/account/sessions/revoke-others", null),
            (HttpMethod.Get, "/users", null),
            (HttpMethod.Get, $"/users/{id}", null),
            (HttpMethod.Put, $"/users/{id}", new { nome = "Trocado pelo HMAC", email = LocalUserSeed.UniqueEmail("hmac") }),
            (HttpMethod.Post, $"/users/{id}/deactivate", null),
            (HttpMethod.Post, $"/users/{id}/activate", null),
            (HttpMethod.Post, $"/users/{id}/sessions/revoke", null),
        };

        foreach (var (method, path, body) in routes)
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = System.Net.Http.Json.JsonContent.Create(body);
            }

            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} → {(int)response.StatusCode}");
        }

        var after = await SignedInUser.ReloadAsync(factory.Services, id);
        Assert.Equal((before.Name, before.Email, before.SecurityStamp, before.Status), (after.Name, after.Email, after.SecurityStamp, after.Status));
        Assert.True(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
    }

    [Fact]
    public async Task ADeactivatedUser_WithAValidAccessToken_Is403()
    {
        var me = await SignedInUser.CreateAsync(factory, "desativado-com-token");
        await LocalUserSeed.DeactivateAsync(factory.Services, me.User.Id);
        var client = SignedInUser.Anonymous(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await TwoFactorTestClient.SendAsync(client, HttpMethod.Get, "/account", me.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await TwoFactorTestClient.SendAsync(client, HttpMethod.Get, "/users", me.AccessToken)).StatusCode);
    }
}
