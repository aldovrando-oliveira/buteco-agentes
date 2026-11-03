using System.Net;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// Consultar e aceitar o convite (spec <c>user-access-links</c>, "Consultar e aceitar o convite"
/// e "O que invalida o link"; design.md da change convites-redefinicao-senha, D4 a D6).
///
/// <para>
/// Verificado contra o defeito (tasks.md):
/// <list type="bullet">
/// <item>6.2: o estado fora da busca e do consumo → <see cref="ALinkRewrittenOnAnActive_IsNotAcceptedAgain"/> reprova;</item>
/// <item>6.3: o <c>ResetAsync</c> retirado do aceite → <see cref="Accepting_ClearsTheEmailLock"/> reprova;</item>
/// <item>6.4: o consumo em dois passos (ler e gravar pela entidade) → <see cref="TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate"/>
/// reprova pela asserção do SQL, em toda execução;</item>
/// <item>6.5: o vencimento fora da busca e do consumo → <see cref="AnExpiredInvitation_IsAnInvalidLink"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class InvitationAcceptTests(InvitationAcceptTests.Fixture factory, ITestOutputHelper output) : IClassFixture<InvitationAcceptTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string NewPassword = "senha-nova-do-convidado";

    private HttpClient Client => SignedInUser.Anonymous(factory);

    [Fact]
    public async Task TheLookup_ShowsNameEmailInviterAndExpiry_AndWritesNothing()
    {
        var caller = await SignedInUser.CreateAsync(factory, "consulta-convite", name: "Aldovrando Oliveira");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id, name: "Marcos Lima");

        var response = await AccessLinkClient.LookupInvitationAsync(Client, invited.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AccessLinkClient.JsonAsync(response);
        Assert.Equal("Marcos Lima", body.GetProperty("nome").GetString());
        Assert.Equal(invited.User.Email, body.GetProperty("email").GetString());
        Assert.Equal("Aldovrando Oliveira", body.GetProperty("convidadoPor").GetString());
        Assert.Equal(invited.User.AccessLinkExpiresAt, body.GetProperty("expiraEm").GetDateTimeOffset());
        var reloaded = await SignedInUser.ReloadAsync(factory.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
        Assert.Null(reloaded.PasswordHash);
    }

    [Fact]
    public async Task APasswordOutsideThePolicy_Is400_AndTheLinkStaysValid()
    {
        var caller = await SignedInUser.CreateAsync(factory, "politica-convite");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await AccessLinkClient.AcceptAsync(Client, invited.Token, new string('x', 11))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
    }

    [Fact]
    public async Task Accepting_Is204_WithoutASession_AndTheLoginThenOpensOne()
    {
        var caller = await SignedInUser.CreateAsync(factory, "aceita", name: "Quem Convidou");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var response = await AccessLinkClient.AcceptAsync(Client, invited.Token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var reloaded = await SignedInUser.ReloadAsync(factory.Services, invited.User.Id);
        Assert.Equal(UserStatus.Active, reloaded.Status);
        Assert.Null(reloaded.AccessLinkHash);
        Assert.Equal(caller.User.Id, reloaded.InvitedBy);

        // O login normal, com a senha recém-criada: o authorize emite o código e a troca dá os tokens.
        var tokens = await IdentityIssuerUserFlow.SignInAsync(Client, factory.Services, invited.User.Email, NewPassword);
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));

        var detail = await AccessLinkClient.JsonAsync(await TwoFactorTestClient.SendAsync(Client, HttpMethod.Get, $"/users/{invited.User.Id}", caller.AccessToken));
        Assert.Equal("ativo", detail.GetProperty("estado").GetString());
        Assert.Equal("Quem Convidou", detail.GetProperty("convidadoPor").GetProperty("nome").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("convite").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.AcceptAsync(Client, invited.Token, "outra-senha-qualquer")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
    }

    [Fact]
    public async Task ALinkRewrittenOnAnActive_IsNotAcceptedAgain()
    {
        var caller = await SignedInUser.CreateAsync(factory, "regrava");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(Client, invited.Token, NewPassword)).StatusCode);

        // O mesmo link regravado à mão na linha, agora ativa: só o estado separa o aceite.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var expiresAt = factory.Clock.GetUtcNow().AddHours(1);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET access_link_hash = {invited.User.AccessLinkHash}, access_link_purpose = 'invitation', access_link_expires_at = {expiresAt} WHERE id = {invited.User.Id}");
        }

        var second = await AccessLinkClient.AcceptAsync(Client, invited.Token, "senha-de-quem-reusou-o-link");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Accepting_ClearsTheEmailLock()
    {
        var caller = await SignedInUser.CreateAsync(factory, "aceita-bloqueado");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, "tentativa-errada");
        }

        Assert.Equal((HttpStatusCode)423, (await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, "tentativa-errada")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(Client, invited.Token, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate()
    {
        var caller = await SignedInUser.CreateAsync(factory, "corrida-aceite");
        var bothSucceeded = 0;
        IReadOnlyList<string> lastCommands = [];
        for (var round = 0; round < 20; round++)
        {
            var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
            HttpResponseMessage[] responses = [];
            lastCommands = await factory.SqlCapture.CaptureAsync(async () => responses = await Task.WhenAll(
                AccessLinkClient.AcceptAsync(Client, invited.Token, "primeira-senha-da-corrida"),
                AccessLinkClient.AcceptAsync(Client, invited.Token, "segunda-senha-da-corrida")));

            var statuses = responses.Select(response => response.StatusCode).Order().ToList();
            bothSucceeded += statuses.All(status => status == HttpStatusCode.NoContent) ? 1 : 0;
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], statuses);

            var winner = responses[0].StatusCode == HttpStatusCode.NoContent ? "primeira-senha-da-corrida" : "segunda-senha-da-corrida";
            var loser = winner.StartsWith("primeira", StringComparison.Ordinal) ? "segunda-senha-da-corrida" : "primeira-senha-da-corrida";
            Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, winner)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(Client, invited.User.Email, loser)).StatusCode);
        }

        output.WriteLine($"rodadas com os dois aceites aprovados: {bothSucceeded} de 20");

        // A metade determinística: cada pedido grava a senha num UPDATE só, com o link, o
        // propósito, o vencimento e o estado no WHERE; o perdedor é esse mesmo UPDATE com 0 linhas.
        var consumptions = lastCommands
            .Where(command => command.Contains("UPDATE users", StringComparison.Ordinal) && command.Contains("password_hash = ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(consumptions);
        foreach (var consumption in consumptions)
        {
            var where = consumption[consumption.IndexOf("WHERE", StringComparison.Ordinal)..];
            foreach (var fragment in new[] { "access_link_hash = @", "access_link_purpose = 'invitation'", "access_link_expires_at > ", "status = 'invited'" })
            {
                Assert.Contains(fragment, where, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task AnExpiredInvitation_IsAnInvalidLink()
    {
        var caller = await SignedInUser.CreateAsync(factory, "vencido-aceite");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id, expiresAt: factory.Clock.GetUtcNow().AddMinutes(-1));
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("redefinicao-vencida"), SignedInUser.DefaultPassword);
        var resetToken = await InvitationSeed.PasswordResetAsync(factory.Services, target.Id, expiresAt: factory.Clock.GetUtcNow().AddMinutes(-1));

        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.AcceptAsync(Client, invited.Token, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupResetAsync(Client, resetToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.CompleteAsync(Client, resetToken, NewPassword)).StatusCode);
        Assert.Equal(UserStatus.Invited, (await SignedInUser.ReloadAsync(factory.Services, invited.User.Id)).Status);
    }

    [Fact]
    public async Task AfterRenewing_TheOldLinkIsInvalid_AndTheNewOneWorks_AndCancellingInvalidates()
    {
        var caller = await SignedInUser.CreateAsync(factory, "renova-consulta");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var renewed = await AccessLinkClient.TokenAsync(await AccessLinkClient.RenewAsync(Client, caller, invited.User.Id, caller.Password));

        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(Client, renewed)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(Client, caller, invited.User.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupInvitationAsync(Client, renewed)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.AcceptAsync(Client, renewed, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task DeactivatingTheInvited_Is409_AndTheLinkStillLooksUp()
    {
        var caller = await SignedInUser.CreateAsync(factory, "desativa-consulta");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await TwoFactorTestClient.SendAsync(Client, HttpMethod.Post, $"/users/{invited.User.Id}/deactivate", caller.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(Client, invited.Token)).StatusCode);
    }
}
