using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>Um usuário com o access token do emissor, para as rotas de 2 etapas (D8, D9).</summary>
public sealed record TwoFactorAccount(User User, string AccessToken, string RefreshToken)
{
    public const string Password = "senha-certa-do-teste";

    public static async Task<TwoFactorAccount> CreateAsync(LocalUsersFixture factory, string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), Password);
        var tokens = await IdentityIssuerUserFlow.SignInAsync(Anonymous(factory), factory.Services, user.Email, Password);
        return new TwoFactorAccount(user, tokens.AccessToken, tokens.RefreshToken);
    }

    public static HttpClient Anonymous(LocalUsersFixture factory)
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    /// <summary>O cadastro pela API: <c>setup</c> e <c>confirm</c> com o código do passo atual.</summary>
    public async Task<(byte[] Secret, IReadOnlyList<string> Codes)> EnrollAsync(LocalUsersFixture factory)
    {
        var client = Anonymous(factory);
        var setup = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/setup", AccessToken, new { senha = Password });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var secret = TwoFactorTestClient.DecodeBase32((await TwoFactorTestClient.JsonAsync(setup)).GetProperty("segredo").GetString()!);

        var confirm = await TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/account/two-factor/confirm", AccessToken,
            new { codigo = TwoFactorTestClient.CodeNow(secret, factory.Clock) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var codes = (await TwoFactorTestClient.JsonAsync(confirm)).GetProperty("codigosRecuperacao").EnumerateArray().Select(code => code.GetString()!).ToList();
        return (secret, codes);
    }
}
