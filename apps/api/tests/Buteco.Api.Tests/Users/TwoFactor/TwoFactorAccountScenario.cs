using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// Um usuário com o access token do emissor, para as rotas de 2 etapas (D8, D9). Ele entra pelo
/// login de verdade, que com a obrigatoriedade cadastra a verificação antes da sessão (change
/// verificacao-2-etapas-obrigatoria, D1): o segredo e os 10 códigos desse cadastro ficam aqui. O
/// código do app aceito no cadastro grava o passo, e só um passo seguinte do relógio vale de novo.
/// </summary>
public sealed record TwoFactorAccount(User User, string AccessToken, string RefreshToken, byte[] Secret, IReadOnlyList<string> RecoveryCodes)
{
    public const string Password = "senha-certa-do-teste";

    public static async Task<TwoFactorAccount> CreateAsync(LocalUsersFixture factory, string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), Password);
        var client = Anonymous(factory);
        var (login, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var enrolled = await TwoFactorTestClient.EnrollAsync(client, factory.Services, pending!);
        TwoFactorTestClient.Remember(user.Email, enrolled.RecoveryCodes);
        var tokens = await TwoFactorTestClient.TokensAsync(client, factory.Services, enrolled.SessionCookie);
        return new TwoFactorAccount(user, tokens.AccessToken, tokens.RefreshToken, enrolled.Secret, enrolled.RecoveryCodes);
    }

    public static HttpClient Anonymous(LocalUsersFixture factory)
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
