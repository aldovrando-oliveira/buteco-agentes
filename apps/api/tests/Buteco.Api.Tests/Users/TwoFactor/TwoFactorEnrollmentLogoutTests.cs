using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O "Voltar" da etapa do cadastro passa pelo logout, que encerra o cookie pendente no servidor
/// (spec <c>issuer-user-login</c>, "Encerramento de sessão revoga a autorização e sempre
/// termina"; design.md da change verificacao-2-etapas-obrigatoria, D3).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.3: com a troca do stamp
/// retirada (o logout só apaga o pendente), <see cref="LogoutDuringTheEnrollment_EndsTheCookieOnTheServer"/> reprova.
/// </para>
/// </summary>
public class TwoFactorEnrollmentLogoutTests(TwoFactorEnrollmentLogoutTests.Fixture factory) : IClassFixture<TwoFactorEnrollmentLogoutTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<User> StoredAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    private static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/connect/logout");
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task LogoutDuringTheEnrollment_EndsTheCookieOnTheServer()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("voltar"), Password);
        var client = Client();
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        var (setup, enrolling) = await TwoFactorTestClient.EnrollmentAsync(client, "setup", pending);
        var secret = TwoFactorTestClient.DecodeBase32((await TwoFactorTestClient.JsonAsync(setup)).GetProperty("segredo").GetString()!);
        var stampBefore = (await StoredAsync(user.Id)).SecurityStamp;

        var logout = await LogoutAsync(client, enrolling!);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/entrar", logout.Headers.Location!.OriginalString);
        var cleared = Assert.Single(logout.Headers.GetValues("Set-Cookie"), value => value.StartsWith("buteco-sessao=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        var stored = await StoredAsync(user.Id);
        Assert.Null(stored.TwoFactorPendingSecret);
        Assert.Null(stored.TwoFactorPendingAt);
        Assert.NotEqual(stampBefore, stored.SecurityStamp);

        // Uma cópia do cookie de antes do logout não confirma mais.
        var (copy, _) = await TwoFactorTestClient.EnrollmentAsync(client, "confirm", enrolling, new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock) });
        Assert.Equal(HttpStatusCode.Unauthorized, copy.StatusCode);
        Assert.Equal("A entrada expirou. Informe e-mail e senha de novo.", await TwoFactorTestClient.TitleAsync(copy));
        Assert.False((await StoredAsync(user.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task LogoutWithTheCompleteCookieOfSomeoneWithTheVerification_KeepsTheStamp()
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("voltar-com-verificacao"), Password);
        var client = Client();
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, factory.Services, user.Email, Password);
        var stampBefore = (await StoredAsync(user.Id)).SecurityStamp;

        var logout = await LogoutAsync(client, cookie);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(stampBefore, (await StoredAsync(user.Id)).SecurityStamp);
    }
}
