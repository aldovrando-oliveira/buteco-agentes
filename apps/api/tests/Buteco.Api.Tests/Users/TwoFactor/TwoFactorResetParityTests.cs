using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.TwoFactor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// A redefinição pela rota do admin e pelo comando de emergência termina no mesmo estado (spec
/// <c>two-factor-totp</c>, "Redefinição da verificação pelo admin" e "Comando de emergência para
/// redefinir a verificação": a mesma implementação, <see cref="TwoFactorReset"/>; design.md da
/// change verificacao-2-etapas-obrigatoria, D6).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 5.3: com a rota numa cópia da
/// operação sem a revogação, <see cref="TheRouteAndTheCommand_EndInTheSameState"/> reprova.
/// </para>
/// </summary>
public class TwoFactorResetParityTests(TwoFactorResetParityTests.Fixture factory) : IClassFixture<TwoFactorResetParityTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private sealed record State(bool Enabled, bool HasSecret, bool HasPending, bool HasLastStep, int Codes, bool StampChanged, IReadOnlyList<string> Authorizations);

    private async Task<State> StateAsync(Guid userId, string stampBefore)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == userId);
        return new State(
            user.IsTwoFactorEnabled,
            user.TwoFactorSecret is not null,
            user.TwoFactorPendingSecret is not null,
            user.TwoFactorLastStep is not null,
            await db.UserRecoveryCodes.CountAsync(code => code.UserId == userId),
            user.SecurityStamp != stampBefore,
            [.. (await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, userId)).Order()]);
    }

    private async Task<string> StampAsync(Guid userId) =>
        (await TwoFactorTestClient.StateAsync(factory.Services, userId)).Stamp;

    [Fact]
    public async Task TheRouteAndTheCommand_EndInTheSameState()
    {
        var admin = await TwoFactorAccount.CreateAsync(factory, "paridade-admin");
        var byRoute = await TwoFactorAccount.CreateAsync(factory, "paridade-rota");
        var byCommand = await TwoFactorAccount.CreateAsync(factory, "paridade-comando");
        var routeStamp = await StampAsync(byRoute.User.Id);
        var commandStamp = await StampAsync(byCommand.User.Id);

        var route = await TwoFactorTestClient.SendAsync(TwoFactorAccount.Anonymous(factory), HttpMethod.Post,
            $"/users/{byRoute.User.Id}/two-factor/reset", admin.AccessToken);
        var command = await EmergencyCommandRunner.RunAsync(factory.PostgresConnectionString, TwoFactorEmergencyCommand.Name, "--email", byCommand.User.Email);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, route.StatusCode);
        Assert.Equal(TwoFactorEmergencyCommand.ResetDone, command.ExitCode);
        var afterRoute = await StateAsync(byRoute.User.Id, routeStamp);
        var afterCommand = await StateAsync(byCommand.User.Id, commandStamp);
        var expected = new State(false, false, false, false, 0, true, [OpenIddict.Abstractions.OpenIddictConstants.Statuses.Revoked]);
        Assert.Equal(expected, afterRoute, new StateComparer());
        Assert.Equal(afterRoute, afterCommand, new StateComparer());
    }

    private sealed class StateComparer : IEqualityComparer<State?>
    {
        public bool Equals(State? x, State? y) =>
            x is not null && y is not null &&
            (x.Enabled, x.HasSecret, x.HasPending, x.HasLastStep, x.Codes, x.StampChanged) ==
            (y.Enabled, y.HasSecret, y.HasPending, y.HasLastStep, y.Codes, y.StampChanged) &&
            x.Authorizations.SequenceEqual(y.Authorizations);

        public int GetHashCode(State? obj) => 0;
    }
}
