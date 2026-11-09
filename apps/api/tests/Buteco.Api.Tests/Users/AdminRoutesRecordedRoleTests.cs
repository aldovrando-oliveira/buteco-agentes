using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// As rotas de Admin releem o papel gravado (spec <c>route-roles</c>, "Prazos da mudança de papel";
/// <c>user-management</c>, <c>user-access-links</c>, <c>two-factor-totp</c>,
/// <c>identity-issuer-overview</c>; design.md da change papeis-no-token, D3, Q1): o token de quem foi
/// rebaixado ainda diz <c>admin</c> e passa pela tabela de papéis, e a rota o recusa na requisição
/// seguinte, pela <see cref="CurrentUser.FindAdminAsync"/>, sem gravar nada. E o papel do alvo não
/// restringe: um Admin redefine a senha e a verificação de outro Admin (Q6).
/// </summary>
public class AdminRoutesRecordedRoleTests(AdminRoutesRecordedRoleTests.Fixture factory) : IClassFixture<AdminRoutesRecordedRoleTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    public static TheoryData<string, string> AdminRoutes() => new()
    {
        { "GET", "/users" },
        { "GET", "/users/{alvo}" },
        { "PUT", "/users/{alvo}" },
        { "POST", "/users/{alvo}/deactivate" },
        { "POST", "/users/{alvo}/activate" },
        { "POST", "/users/{alvo}/sessions/revoke" },
        { "PUT", "/users/{alvo}/role" },
        { "POST", "/users/{alvo}/two-factor/reset" },
        { "POST", "/users/invitations" },
        { "POST", "/users/{alvo}/invitation/renew" },
        { "POST", "/users/{alvo}/invitation/cancel" },
        { "POST", "/users/{alvo}/password-reset" },
        { "GET", "/identity" },
    };

    [Theory]
    [MemberData(nameof(AdminRoutes))]
    public async Task ADemotedAdmin_IsRefusedOnTheNextRequest_WithTheTokenFromBefore(string method, string route)
    {
        var demoted = await SignedInUser.CreateAsync(factory, "rebaixado");
        var target = await SignedInUser.CreateAsync(factory, "alvo-rebaixado");
        var targetBefore = await SignedInUser.ReloadAsync(factory.Services, target.User.Id);
        await LocalUserSeed.SetRoleAsync(factory.Services, demoted.User.Id, UserRole.Operator);

        var response = await TwoFactorTestClient.SendAsync(
            SignedInUser.Anonymous(factory), new HttpMethod(method), route.Replace("{alvo}", target.User.Id.ToString(), StringComparison.Ordinal),
            demoted.AccessToken, BodyFor(method, route, demoted.Password));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CurrentUser.ForbiddenMessage, await TwoFactorTestClient.TitleAsync(response));
        var targetAfter = await SignedInUser.ReloadAsync(factory.Services, target.User.Id);
        Assert.Equal(targetBefore.Status, targetAfter.Status);
        Assert.Equal(targetBefore.Name, targetAfter.Name);
        Assert.Equal(targetBefore.SecurityStamp, targetAfter.SecurityStamp);
        Assert.Null(targetAfter.AccessLinkHash);
    }

    [Fact]
    public async Task AnAdmin_GeneratesThePasswordResetOfAnotherAdmin()
    {
        var caller = await SignedInUser.CreateAsync(factory, "admin-redefine");
        var other = await SignedInUser.CreateAsync(factory, "admin-alvo");

        var response = await TwoFactorTestClient.SendAsync(
            SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{other.User.Id}/password-reset", caller.AccessToken, new { senha = caller.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnAdmin_ResetsTheTwoFactorOfAnotherAdmin()
    {
        var caller = await SignedInUser.CreateAsync(factory, "admin-redefine-2e");
        var other = await SignedInUser.CreateAsync(factory, "admin-alvo-2e");

        var response = await TwoFactorTestClient.SendAsync(
            SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{other.User.Id}/two-factor/reset", caller.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // Corpos válidos, para a recusa vir do papel e não da validação do corpo.
    private static object? BodyFor(string method, string route, string password) => route switch
    {
        _ when method == "GET" => null,
        _ when route.EndsWith("/role", StringComparison.Ordinal) => new { papel = "leitor" },
        _ when route == "/users/invitations" => new { nome = "Convidada", email = LocalUserSeed.UniqueEmail("convite-rebaixado"), papel = "leitor", senha = password },
        _ when route == "/users/{alvo}" => new { nome = "Nome Novo", email = LocalUserSeed.UniqueEmail("editado") },
        _ when route.EndsWith("/renew", StringComparison.Ordinal) || route.EndsWith("/password-reset", StringComparison.Ordinal) => new { senha = password },
        _ => null,
    };
}
