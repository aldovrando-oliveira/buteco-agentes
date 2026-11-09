using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O link pela rota do admin e pelo comando de emergência termina no mesmo estado (spec
/// <c>user-access-links</c>, "Gerar o link de redefinição de senha", cenários "Mesmo efeito da rota e
/// do comando" e "Dois links em sequência"; design.md da change comando-password-reset-link, D1 e D7:
/// a mesma escrita, <see cref="PasswordResetLink"/>).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 2.4: com a rota numa cópia da
/// escrita que troca o carimbo de segurança, <see cref="TheRouteAndTheCommand_EndInTheSameState"/> reprova.
/// </para>
/// </summary>
public class PasswordResetLinkParityTests(PasswordResetLinkParityTests.Fixture factory) : IClassFixture<PasswordResetLinkParityTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    /// <summary>
    /// O que a escrita do link pode mudar, medido do mesmo jeito para as duas entradas. O vencimento
    /// entra como a distância até o relógio de quem gerou: a rota usa o relógio falso do host, e o
    /// comando, num processo à parte, o do sistema.
    /// </summary>
    private sealed record State(
        bool PreviousLinkWorks, bool NewLinkWorks, AccessLinkPurpose? Purpose, bool ExpiresInADay,
        bool StampChanged, bool PasswordHashChanged, bool CurrentPasswordAccepted, IReadOnlyList<string> Authorizations);

    private HttpClient Client => SignedInUser.Anonymous(factory);

    private async Task<bool> LinkWorksAsync(string token) =>
        (await AccessLinkClient.LookupResetAsync(Client, token)).StatusCode == HttpStatusCode.OK;

    private async Task<State> StateAsync(SignedInUser target, User before, string previous, string link, DateTimeOffset generatedAt)
    {
        var after = await SignedInUser.ReloadAsync(factory.Services, target.User.Id);
        var login = await LocalUserSeed.PostSessionAsync(Client, target.User.Email, target.Password);
        var lifetime = after.AccessLinkExpiresAt!.Value - generatedAt;
        return new State(
            await LinkWorksAsync(previous),
            await LinkWorksAsync(AccessLinkClient.TokenOf(link)),
            after.AccessLinkPurpose,
            Math.Abs((lifetime - AccessLinkToken.PasswordResetLifetime).TotalMinutes) < 1,
            after.SecurityStamp != before.SecurityStamp,
            after.PasswordHash != before.PasswordHash,
            login.StatusCode == HttpStatusCode.OK,
            [.. (await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, target.User.Id)).Order()]);
    }

    [Fact]
    public async Task TheRouteAndTheCommand_EndInTheSameState()
    {
        var admin = await SignedInUser.CreateAsync(factory, "paridade-senha-admin");
        var byRoute = await SignedInUser.CreateAsync(factory, "paridade-senha-rota");
        var byCommand = await SignedInUser.CreateAsync(factory, "paridade-senha-comando");
        var routePrevious = await InvitationSeed.PasswordResetAsync(factory.Services, byRoute.User.Id);
        var commandPrevious = await InvitationSeed.PasswordResetAsync(factory.Services, byCommand.User.Id);
        var routeBefore = await SignedInUser.ReloadAsync(factory.Services, byRoute.User.Id);
        var commandBefore = await SignedInUser.ReloadAsync(factory.Services, byCommand.User.Id);

        var route = await AccessLinkClient.ResetAsync(Client, admin, byRoute.User.Id, admin.Password);
        var commandStartedAt = DateTimeOffset.UtcNow;
        var command = await PasswordResetLinkCommandTests.RunAsync(factory, PasswordResetLinkCommand.Name, "--email", byCommand.User.Email);

        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
        Assert.Equal(PasswordResetLinkCommand.Issued, command.ExitCode);
        var afterRoute = await StateAsync(byRoute, routeBefore, routePrevious, await AccessLinkClient.LinkAsync(route), factory.Clock.GetUtcNow());
        var afterCommand = await StateAsync(byCommand, commandBefore, commandPrevious, PasswordResetLinkCommandTests.LinkOf(command), commandStartedAt);
        var expected = new State(false, true, AccessLinkPurpose.PasswordReset, true, false, false, true, [Statuses.Valid]);
        Assert.Equal(expected, afterRoute, new StateComparer());
        Assert.Equal(afterRoute, afterCommand, new StateComparer());
    }

    [Fact]
    public async Task TwoLinksInARow_ByTheRouteThenTheCommand_OnlyTheSecondWorks()
    {
        var admin = await SignedInUser.CreateAsync(factory, "sequencia-admin");
        var target = await SignedInUser.CreateAsync(factory, "sequencia-alvo");

        var first = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, admin, target.User.Id, admin.Password));
        var second = await PasswordResetLinkCommandTests.RunAsync(factory, PasswordResetLinkCommand.Name, "--email", target.User.Email);

        Assert.Equal(PasswordResetLinkCommand.Issued, second.ExitCode);
        Assert.False(await LinkWorksAsync(first));
        Assert.True(await LinkWorksAsync(AccessLinkClient.TokenOf(PasswordResetLinkCommandTests.LinkOf(second))));
    }

    private sealed class StateComparer : IEqualityComparer<State?>
    {
        public bool Equals(State? x, State? y) =>
            x is not null && y is not null &&
            (x.PreviousLinkWorks, x.NewLinkWorks, x.Purpose, x.ExpiresInADay, x.StampChanged, x.PasswordHashChanged, x.CurrentPasswordAccepted) ==
            (y.PreviousLinkWorks, y.NewLinkWorks, y.Purpose, y.ExpiresInADay, y.StampChanged, y.PasswordHashChanged, y.CurrentPasswordAccepted) &&
            x.Authorizations.SequenceEqual(y.Authorizations);

        public int GetHashCode(State? obj) => 0;
    }
}
