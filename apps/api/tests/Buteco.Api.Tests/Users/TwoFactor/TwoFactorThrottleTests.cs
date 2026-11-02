using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// As falhas do código no bloqueio por e-mail (spec <c>local-users</c>, "Bloqueio progressivo
/// por e-mail" e "Último acesso registrado"; design.md da change verificacao-2-etapas-totp,
/// D7). Um caso por cenário.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 7.4: com a senha certa
/// zerando a contagem mesmo com o código pendente,
/// <see cref="RightPasswordAndWrongCode_Alternating_StillLocks"/> reprova.
/// </para>
/// </summary>
public class TwoFactorThrottleTests(TwoFactorThrottleTests.Fixture factory) : IClassFixture<TwoFactorThrottleTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Password = "senha-certa-do-teste";

    private async Task<(Buteco.Api.Users.User User, TwoFactorTestClient.Enabled TwoFactor)> UserWithTheVerificationAsync(string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail(prefix), Password);
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(factory.Services, user.Id);
        TwoFactorTestClient.NextStep(factory.Clock);
        return (user, enabled);
    }

    // Cinco passos fora da janela: nunca é o código certo.
    private string WrongCode(byte[] secret) => TwoFactorTestClient.CodeNow(secret, factory.Clock, stepOffset: 5);

    [Fact]
    public async Task RightPasswordAndWrongCode_Alternating_StillLocks()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("alternado");
        var client = IdentityIssuerUserFlow.Client(factory);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var (password, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            var (code, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, WrongCode(twoFactor.Secret));
            Assert.Equal(HttpStatusCode.Unauthorized, code.StatusCode);
        }

        var (afterwards, _) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        Assert.Equal((HttpStatusCode)423, afterwards.StatusCode);
        Assert.NotNull(afterwards.Headers.RetryAfter);
    }

    [Fact]
    public async Task DuringTheLock_TheSecondStepDoesNotCheckTheCode()
    {
        var (user, twoFactor) = await UserWithTheVerificationAsync("bloqueado");
        var client = IdentityIssuerUserFlow.Client(factory);
        var (_, pending) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await TwoFactorTestClient.SecondFactorAsync(client, pending, WrongCode(twoFactor.Secret));
        }

        var (locked, _) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(twoFactor.Secret, factory.Clock));

        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.NotNull(locked.Headers.RetryAfter);
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(0, stored.TwoFactorLastStep);
    }

    [Fact]
    public async Task RightPasswordAlone_DoesNotChangeTheLastAccess()
    {
        var (user, _) = await UserWithTheVerificationAsync("ultimo-acesso");
        var client = IdentityIssuerUserFlow.Client(factory);

        var (password, _) = await TwoFactorTestClient.PasswordStepAsync(client, user.Email, Password);

        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Null(stored.LastAccessAt);
    }
}
