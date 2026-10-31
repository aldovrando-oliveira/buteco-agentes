using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Login;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O <c>POST /auth/session</c> no caminho feliz e nas recusas por credencial (specs
/// <c>issuer-user-login</c>, "Rota de login abre a sessão do emissor", e <c>local-users</c>,
/// "Resposta de login não revela se a conta existe"; design.md da change
/// usuarios-locais-login-emissor, D1 e D4).
/// </summary>
public class SessionLoginTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => factory.CreateClient(new() { HandleCookies = false });

    [Fact]
    public async Task ValidLogin_Returns204_WithTheSessionCookie()
    {
        var email = LocalUserSeed.UniqueEmail("valido");
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        var response = await LocalUserSeed.PostSessionAsync(Client(), email.ToUpperInvariant() + " ", Password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("buteco-sessao=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var expires = DateTimeOffset.Parse(cookie.Split(';').Single(part => part.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Split('=', 2)[1]);
        Assert.InRange(expires - factory.Clock.GetUtcNow(), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.InRange(stored.LastAccessAt!.Value, factory.Clock.GetUtcNow() - TimeSpan.FromMilliseconds(1), factory.Clock.GetUtcNow());
    }

    [Fact]
    public async Task WrongPassword_Returns401Generic_WithoutCookie()
    {
        var email = LocalUserSeed.UniqueEmail("errada");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        var response = await LocalUserSeed.PostSessionAsync(Client(), email, "outra-senha");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains(SessionEndpoints.InvalidCredentialsMessage, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownEmail_AndWrongPassword_AnswerTheSame()
    {
        var email = LocalUserSeed.UniqueEmail("existente");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        var wrong = await LocalUserSeed.PostSessionAsync(Client(), email, "outra-senha");
        var unknown = await LocalUserSeed.PostSessionAsync(Client(), LocalUserSeed.UniqueEmail("sem-conta"), "outra-senha");

        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownHashFormat_AnswersAsWrongPassword()
    {
        var email = LocalUserSeed.UniqueEmail("formato");
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE users SET password_hash = 'formato-desconhecido' WHERE id = {user.Id}");
        }

        var response = await LocalUserSeed.PostSessionAsync(Client(), email, Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
