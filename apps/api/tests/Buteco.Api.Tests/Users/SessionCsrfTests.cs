using System.Net;
using System.Text;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// As barreiras de CSRF do <c>POST /auth/session</c> (spec <c>issuer-user-login</c>, "A rota
/// de login só aceita JSON da origem do emissor"; design.md da change
/// usuarios-locais-login-emissor, D1). Em nenhuma recusa a senha é verificada, e a
/// contagem de falhas do e-mail não muda.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a conferência de <c>Origin</c> retirada
/// do <c>SessionRequestGuards</c>, o caso de outra origem reprova (registro no tasks.md, 5.4).
/// </para>
/// </summary>
public class SessionCsrfTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => factory.CreateClient(new() { HandleCookies = false });

    private async Task<bool> HasThrottleRowAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AnyAsync(row => row.NormalizedEmail == email);
    }

    [Fact]
    public async Task OtherOrigin_Is403_WithoutCookie_AndTheCountDoesNotChange()
    {
        var email = LocalUserSeed.UniqueEmail("outra-origem");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        var right = await LocalUserSeed.PostSessionAsync(Client(), email, Password, origin: "https://outro.exemplo");
        var wrong = await LocalUserSeed.PostSessionAsync(Client(), email, "errada", origin: "https://outro.exemplo");

        Assert.Equal(HttpStatusCode.Forbidden, right.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.False(right.Headers.Contains("Set-Cookie"));
        Assert.False(await HasThrottleRowAsync(email));
    }

    [Fact]
    public async Task MissingOrigin_Is403()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session")
        {
            Content = new StringContent("""{"email":"a@exemplo.test","senha":"x"}""", Encoding.UTF8, "application/json"),
        };

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "email=a%40exemplo.test&senha=x")]
    [InlineData("text/plain", """{"email":"a@exemplo.test","senha":"x"}""")]
    public async Task NotJson_Is415_WithoutCookie(string contentType, string body)
    {
        var email = LocalUserSeed.UniqueEmail("formulario");
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session")
        {
            Content = new StringContent(body.Replace("a@exemplo.test", email).Replace("a%40exemplo.test", Uri.EscapeDataString(email)), Encoding.UTF8, contentType),
        };
        request.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.False(await HasThrottleRowAsync(email));
    }
}
