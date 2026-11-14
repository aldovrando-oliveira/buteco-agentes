using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O aceite pelo provedor e o aceite por senha disputam o mesmo convite (spec <c>federated-login</c>, "O aceite
/// pelo provedor e o aceite por senha disputam o mesmo convite" e "Decisão do aceite de convite no retorno";
/// design.md da change aceite-convite-google, D4). A pausa do fixture põe o outro lado exatamente entre a
/// leitura do convite e o consumo. Guardas verificados contra o defeito (tasks.md, 5.2 a 5.5).
/// </summary>
public class FederatedInvitationConcurrencyTests(FederatedInvitationFixture fixture, ITestOutputHelper output) : IClassFixture<FederatedInvitationFixture>
{
    [Fact]
    public async Task PasswordAndProvider_ThePasswordWins_NoLink()
    {
        var (_, invited) = await InvitedAsync("senha-vence");
        var browser = GoogleFlow.Client(fixture);
        HttpStatusCode? password = null;
        fixture.Pause.Arm(invited.User.Id, async _ => password = (await AccessLinkClient.AcceptAsync(browser, invited.Token, "senha-que-chegou-antes")).StatusCode);

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));

        Assert.Equal(HttpStatusCode.NoContent, password);
        Assert.Equal("/convite?recusa=convite-invalido&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        var user = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.PasswordHash);
    }

    [Fact]
    public async Task PasswordAndProvider_TheProviderWins_NoPassword()
    {
        var (_, invited) = await InvitedAsync("google-vence");

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));
        var password = await AccessLinkClient.AcceptAsync(GoogleFlow.Client(fixture), invited.Token, "senha-que-chegou-depois");

        Assert.StartsWith("/entrar?", returned.Location, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, password.StatusCode);
        Assert.Single(await LinksOfAsync(invited.User.Id));
        Assert.Null((await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id)).PasswordHash);
    }

    [Fact]
    public async Task PasswordAndProvider_AtTheSameTime_OnlyOneWins_InTwentyRounds()
    {
        var caller = await SignedInUser.CreateAsync(fixture, "corrida-solta");
        var providerWins = 0;
        for (var round = 0; round < 20; round++)
        {
            var invited = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);
            var browser = GoogleFlow.Client(fixture);
            var started = await GoogleFlow.StartInvitationAsync(browser, invited.Token);
            var code = GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(invited.User.Email));

            var providerTask = GoogleFlow.ReturnAsync(browser, code, started.Query["state"], started.FlowCookie);
            var passwordTask = AccessLinkClient.AcceptAsync(GoogleFlow.Client(fixture), invited.Token, "senha-da-corrida-solta");
            await Task.WhenAll(providerTask, passwordTask);

            var accepted = providerTask.Result.Location.StartsWith("/entrar?federado=", StringComparison.Ordinal);
            var passwordAccepted = passwordTask.Result.StatusCode == HttpStatusCode.NoContent;
            Assert.True(accepted ^ passwordAccepted, $"rodada {round}: provedor {providerTask.Result.Location}, senha {(int)passwordTask.Result.StatusCode}");

            var user = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
            var links = await LinksOfAsync(invited.User.Id);
            Assert.Equal(accepted ? 1 : 0, links.Count);
            Assert.Equal(passwordAccepted, user.PasswordHash is not null);
            providerWins += accepted ? 1 : 0;
        }

        output.WriteLine($"rodadas com o provedor vencendo: {providerWins} de 20");
    }

    [Fact]
    public async Task TwoProviderReturns_OneLinkOnly()
    {
        var (_, invited) = await InvitedAsync("google-google");
        var browserA = GoogleFlow.Client(fixture);
        var browserB = GoogleFlow.Client(fixture);
        var startedA = await GoogleFlow.StartInvitationAsync(browserA, invited.Token);
        var startedB = await GoogleFlow.StartInvitationAsync(browserB, invited.Token);
        var codeA = GoogleFlow.Consent(fixture, startedA, GoogleFlow.NewAccount(invited.User.Email));
        var codeB = GoogleFlow.Consent(fixture, startedB, GoogleFlow.NewAccount(invited.User.Email));

        // O retorno B termina inteiro dentro da pausa do A, entre a leitura e o consumo do A.
        GoogleFlow.Returned? returnedB = null;
        fixture.Pause.Arm(invited.User.Id, async _ => returnedB = await GoogleFlow.ReturnAsync(browserB, codeB, startedB.Query["state"], startedB.FlowCookie));
        var returnedA = await GoogleFlow.ReturnAsync(browserA, codeA, startedA.Query["state"], startedA.FlowCookie);

        Assert.StartsWith("/entrar?federado=corporativo", returnedB!.Location, StringComparison.Ordinal);
        Assert.Equal("/convite?recusa=convite-invalido&provedor=corporativo", returnedA.Location);
        Assert.Null(returnedA.Cookie("buteco-sessao"));
        Assert.Single(await LinksOfAsync(invited.User.Id));
    }

    [Fact]
    public async Task TheLinkThatFails_UndoesTheConsumption()
    {
        var (_, invited) = await InvitedAsync("vinculo-falha");
        var account = GoogleFlow.NewAccount(invited.User.Email);
        var other = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("ganhou-o-par"), "senha-de-quem-ganhou");
        fixture.Pause.Arm(invited.User.Id, _ => GoogleFlow.LinkAsync(fixture.Services, other.Id, account));

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, account);

        Assert.Equal("/convite?recusa=em-uso&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        var user = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, user.Status);
        Assert.Equal(invited.User.AccessLinkHash, user.AccessLinkHash);
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(GoogleFlow.Client(fixture), invited.Token, "senha-depois-do-em-uso")).StatusCode);
    }

    [Fact]
    public async Task TheEmailEditedInTheMiddle_IsNotAccepted()
    {
        var (_, invited) = await InvitedAsync("email-editado");
        var newEmail = LocalUserSeed.UniqueEmail("email-novo");
        fixture.Pause.Arm(invited.User.Id, async id =>
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == id)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.Email, newEmail).SetProperty(user => user.NormalizedEmail, EmailNormalization.Normalize(newEmail)));
        });

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));

        Assert.Equal("/convite?recusa=convite-invalido&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        Assert.Equal(UserStatus.Invited, (await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id)).Status);
    }

    [Fact]
    public async Task ANewLinkInTheMiddle_IsNotConsumedByTheOldOne()
    {
        var (caller, invited) = await InvitedAsync("link-novo-meio");
        var browser = GoogleFlow.Client(fixture);
        string? newToken = null;
        fixture.Pause.Arm(invited.User.Id, async id => newToken = await AccessLinkClient.TokenAsync(await AccessLinkClient.RenewAsync(browser, caller, id, caller.Password)));

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));

        Assert.Equal("/convite?recusa=convite-invalido&provedor=corporativo", returned.Location);
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        Assert.Equal(UserStatus.Invited, (await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(browser, newToken!)).StatusCode);
    }

    private async Task<(SignedInUser Caller, InvitationSeed.Seeded Invited)> InvitedAsync(string prefix)
    {
        var caller = await SignedInUser.CreateAsync(fixture, prefix);
        return (caller, await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id));
    }

    private async Task<List<Buteco.Api.Users.Federated.UserFederatedIdentity>> LinksOfAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserFederatedIdentities.AsNoTracking().Where(link => link.UserId == userId).ToListAsync();
    }
}
