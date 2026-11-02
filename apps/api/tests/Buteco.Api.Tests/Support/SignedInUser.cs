using System.Net;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Um usuário com uma sessão do emissor pelo fluxo real (design.md da change
/// rotas-conta-usuarios, D10): o par de tokens e o <c>oi_au_id</c> da sessão, lido do access
/// token como o <c>apps/api</c> o lê.
/// </summary>
public sealed record SignedInUser(User User, string Password, IdentityIssuerUserFlow.Tokens Tokens)
{
    public const string DefaultPassword = "senha-certa-do-teste";

    public string AccessToken => Tokens.AccessToken;

    public string RefreshToken => Tokens.RefreshToken;

    public string AuthorizationId => AuthorizationIdOf(Tokens.AccessToken);

    public static async Task<SignedInUser> CreateAsync(LocalUsersFixture factory, string prefix, string password = DefaultPassword, string name = "Pessoa de Teste")
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), password, name);
        return await SignInAsync(factory, user, password);
    }

    public static async Task<SignedInUser> SignInAsync(LocalUsersFixture factory, User user, string password) =>
        new(user, password, await IdentityIssuerUserFlow.SignInAsync(Anonymous(factory), factory.Services, user.Email, password));

    /// <summary>Outra sessão da mesma pessoa, por um login novo.</summary>
    public Task<SignedInUser> SignInAgainAsync(LocalUsersFixture factory) => SignInAsync(factory, User, Password);

    public static HttpClient Anonymous(LocalUsersFixture factory)
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public static string AuthorizationIdOf(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.GetProperty("oi_au_id").GetString()!;
    }

    public static async Task<bool> RenewsAsync(LocalUsersFixture factory, string refreshToken)
    {
        var response = await IdentityIssuerUserFlow.RefreshAsync(Anonymous(factory), refreshToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return true;
        }

        Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        return false;
    }

    public static async Task<User> ReloadAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId);
    }
}
