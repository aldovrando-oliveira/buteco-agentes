using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.AccessLinks;
using Microsoft.EntityFrameworkCore;
using Buteco.Api.Users.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// A redefinição de senha pelo admin (spec <c>user-access-links</c>, "Gerar o link de redefinição
/// de senha"; design.md da change convites-redefinicao-senha, D7).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 5.4): a recusa de si mesmo retirada →
/// <see cref="ForOneself_Is403_AndNothingIsCounted"/> reprova.
/// </para>
///
/// <para>
/// E a conclusão (tasks.md, 4.3, 6.6 e 6.7), um defeito de cada vez: a revogação retirada →
/// <see cref="Completing_ChangesThePassword_EndsEverySession_AndOpensNone"/> reprova (a lista mostra
/// sessões); a troca do stamp retirada → <see cref="Completing_RefusesASessionCookieInFlight"/> reprova
/// (a renovação não serve de guarda do stamp: a revogação já a derruba sozinha); <c>Deactivate</c>
/// sem apagar o link → <see cref="Deactivating_InvalidatesTheLink_AndReactivatingDoesNotBringItBack"/>
/// reprova; <c>ChangePassword</c> sem apagar o link → <see cref="ChangingTheOwnPassword_InvalidatesThePendingLink"/>
/// reprova; o consumo em dois passos → <see cref="TwoConcurrentCompletions_OneWins_AndTheConsumptionIsOneConditionalUpdate"/>
/// reprova pela asserção do SQL, em toda execução.
/// </para>
/// </summary>
public class PasswordResetTests(PasswordResetTests.Fixture factory, ITestOutputHelper output) : IClassFixture<PasswordResetTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string NewPassword = "senha-nova-pela-redefinicao";

    private HttpClient Client => SignedInUser.Anonymous(factory);

    [Fact]
    public async Task Generating_Returns200_With24Hours_AndTheCurrentPasswordKeepsWorking()
    {
        var caller = await SignedInUser.CreateAsync(factory, "redefine");
        var target = await SignedInUser.CreateAsync(factory, "redefine-alvo");

        var response = await AccessLinkClient.ResetAsync(Client, caller, target.User.Id, caller.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AccessLinkClient.JsonAsync(response);
        Assert.Contains("/redefinir-senha#", body.GetProperty("link").GetString(), StringComparison.Ordinal);
        Assert.Equal(factory.Clock.GetUtcNow() + AccessLinkToken.PasswordResetLifetime, body.GetProperty("expiraEm").GetDateTimeOffset());
        var stored = await SignedInUser.ReloadAsync(factory.Services, target.User.Id);
        Assert.Equal(AccessLinkPurpose.PasswordReset, stored.AccessLinkPurpose);
        Assert.Equal(target.User.SecurityStamp, stored.SecurityStamp);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, target.User.Email, target.Password)).StatusCode);
        Assert.True(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
    }

    [Fact]
    public async Task ForOneself_Is403_AndNothingIsCounted()
    {
        var caller = await SignedInUser.CreateAsync(factory, "redefine-a-si");

        var response = await AccessLinkClient.ResetAsync(Client, caller, caller.User.Id, "senha-errada-de-proposito");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, caller.User.Id)).AccessLinkHash);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles
            .AnyAsync(row => row.NormalizedEmail == caller.User.NormalizedEmail));
    }

    [Fact]
    public async Task ForTheInvitedAndTheDeactivated_Is409()
    {
        var caller = await SignedInUser.CreateAsync(factory, "redefine-409");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        var deactivated = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("desativado"), SignedInUser.DefaultPassword);
        await LocalUserSeed.DeactivateAsync(factory.Services, deactivated.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await AccessLinkClient.ResetAsync(Client, caller, invited.User.Id, caller.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AccessLinkClient.ResetAsync(Client, caller, deactivated.Id, caller.Password)).StatusCode);
        Assert.Equal(AccessLinkPurpose.Invitation, (await SignedInUser.ReloadAsync(factory.Services, invited.User.Id)).AccessLinkPurpose);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, deactivated.Id)).AccessLinkHash);
    }

    [Fact]
    public async Task UnknownId_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "redefine-404");

        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.ResetAsync(Client, caller, Guid.NewGuid(), caller.Password)).StatusCode);
    }

    [Fact]
    public async Task TheLookup_ShowsNameEmailAndExpiry()
    {
        var caller = await SignedInUser.CreateAsync(factory, "consulta-redefinicao");
        var target = await SignedInUser.CreateAsync(factory, "consulta-redefinicao-alvo", name: "Rafael Duarte");
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.User.Id, caller.Password));

        var response = await AccessLinkClient.LookupResetAsync(Client, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AccessLinkClient.JsonAsync(response);
        Assert.Equal("Rafael Duarte", body.GetProperty("nome").GetString());
        Assert.Equal(target.User.Email, body.GetProperty("email").GetString());
        Assert.Equal(factory.Clock.GetUtcNow() + AccessLinkToken.PasswordResetLifetime, body.GetProperty("expiraEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Completing_ChangesThePassword_EndsEverySession_AndOpensNone()
    {
        var caller = await SignedInUser.CreateAsync(factory, "conclui");
        var target = await SignedInUser.CreateAsync(factory, "conclui-alvo");
        var second = await target.SignInAgainAsync(factory);
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.User.Id, caller.Password));

        Assert.Equal(HttpStatusCode.BadRequest, (await AccessLinkClient.CompleteAsync(Client, token, new string('x', 11))).StatusCode);
        var response = await AccessLinkClient.CompleteAsync(Client, token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(Client, target.User.Email, target.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, target.User.Email, NewPassword)).StatusCode);
        Assert.False(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
        Assert.False(await SignedInUser.RenewsAsync(factory, second.RefreshToken));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<UserSessions>().SummarizeAsync(target.User.Id, CancellationToken.None)).Ativas);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.CompleteAsync(Client, token, "mais-uma-senha-nova")).StatusCode);
    }

    [Fact]
    public async Task Completing_RefusesASessionCookieInFlight()
    {
        var caller = await SignedInUser.CreateAsync(factory, "conclui-cookie");
        var target = await SignedInUser.CreateAsync(factory, "conclui-cookie-alvo");
        var client = SignedInUser.Anonymous(factory);
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, target.User.Email, target.Password);
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.User.Id, caller.Password));

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CompleteAsync(Client, token, NewPassword)).StatusCode);

        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Completing_KeepsTheTwoStepVerification()
    {
        var caller = await SignedInUser.CreateAsync(factory, "conclui-2e");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("conclui-2e-alvo"), SignedInUser.DefaultPassword);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, target.Id);
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.Id, caller.Password));

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CompleteAsync(Client, token, NewPassword)).StatusCode);

        var login = await LocalUserSeed.PostSessionAsync(Client, target.Email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("totp", (await AccessLinkClient.JsonAsync(login)).GetProperty("segundoPasso").GetString());
    }

    [Fact]
    public async Task Completing_ClearsTheEmailLock()
    {
        var caller = await SignedInUser.CreateAsync(factory, "conclui-bloqueado");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("conclui-bloqueado-alvo"), SignedInUser.DefaultPassword);
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.Id, caller.Password));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LocalUserSeed.PostSessionAsync(Client, target.Email, "tentativa-errada");
        }

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CompleteAsync(Client, token, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client, target.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Deactivating_InvalidatesTheLink_AndReactivatingDoesNotBringItBack()
    {
        var caller = await SignedInUser.CreateAsync(factory, "desativa-link");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("desativa-link-alvo"), SignedInUser.DefaultPassword);
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.Id, caller.Password));

        Assert.Equal(HttpStatusCode.OK, (await TwoFactorTestClient.SendAsync(Client, HttpMethod.Post, $"/users/{target.Id}/deactivate", caller.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupResetAsync(Client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TwoFactorTestClient.SendAsync(Client, HttpMethod.Post, $"/users/{target.Id}/activate", caller.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupResetAsync(Client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.CompleteAsync(Client, token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangingTheOwnPassword_InvalidatesThePendingLink()
    {
        var caller = await SignedInUser.CreateAsync(factory, "troca-propria");
        var target = await SignedInUser.CreateAsync(factory, "troca-propria-alvo");
        var token = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(Client, caller, target.User.Id, caller.Password));

        var change = await TwoFactorTestClient.SendAsync(Client, HttpMethod.Post, "/account/password", target.AccessToken,
            new { senhaAtual = target.Password, novaSenha = "a-senha-que-a-pessoa-escolheu" });

        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupResetAsync(Client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.CompleteAsync(Client, token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task TwoConcurrentCompletions_OneWins_AndTheConsumptionIsOneConditionalUpdate()
    {
        var caller = await SignedInUser.CreateAsync(factory, "corrida-conclusao");
        var bothSucceeded = 0;
        IReadOnlyList<string> lastCommands = [];
        for (var round = 0; round < 20; round++)
        {
            var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("corrida-alvo"), SignedInUser.DefaultPassword);
            var token = await InvitationSeed.PasswordResetAsync(factory.Services, target.Id);
            HttpResponseMessage[] responses = [];
            lastCommands = await factory.SqlCapture.CaptureAsync(async () => responses = await Task.WhenAll(
                AccessLinkClient.CompleteAsync(Client, token, "primeira-senha-da-corrida"),
                AccessLinkClient.CompleteAsync(Client, token, "segunda-senha-da-corrida")));

            var statuses = responses.Select(response => response.StatusCode).Order().ToList();
            bothSucceeded += statuses.All(status => status == HttpStatusCode.NoContent) ? 1 : 0;
            Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NotFound], statuses);
        }

        output.WriteLine($"rodadas com as duas conclusões aprovadas: {bothSucceeded} de 20");

        var consumptions = lastCommands
            .Where(command => command.Contains("UPDATE users", StringComparison.Ordinal) && command.Contains("password_hash = ", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(consumptions);
        foreach (var consumption in consumptions)
        {
            var where = consumption[consumption.IndexOf("WHERE", StringComparison.Ordinal)..];
            foreach (var fragment in new[] { "access_link_hash = @", "access_link_purpose = 'password_reset'", "access_link_expires_at > ", "status = 'active'" })
            {
                Assert.Contains(fragment, where, StringComparison.Ordinal);
            }
        }
    }
}
