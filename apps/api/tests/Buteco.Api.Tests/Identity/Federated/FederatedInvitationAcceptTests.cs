using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O retorno do aceite de convite pelo provedor (specs <c>federated-login</c>, "Decisão do aceite de convite no
/// retorno" e "As recusas do aceite não criam sessão e voltam ao convite"; <c>issuer-user-login</c>, "A sessão
/// pendente também nasce do aceite de convite pelo provedor"; <c>local-users</c>, "Usuário ativo sem senha";
/// design.md da change aceite-convite-google, D2 e D3). Guardas verificados contra o defeito (tasks.md, 4.3 a 4.8).
/// </summary>
public class FederatedInvitationAcceptTests(FederatedInvitationFixture fixture) : IClassFixture<FederatedInvitationFixture>
{
    private const string Session = "buteco-sessao";

    // ---- O aceite ----

    [Fact]
    public async Task Accepting_OpensThePendingSession_AndLeavesTheUserActiveWithoutPassword_WithTheLink()
    {
        var (caller, invited) = await InvitedAsync("aceite", UserRole.Operator);
        var account = GoogleFlow.NewAccount(invited.User.Email);

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, account);

        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.Equal("/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito", returned.Location);
        Assert.NotNull(returned.Cookie(Session));
        Assert.Contains("samesite=strict", returned.SetCookieHeader(Session)!, StringComparison.OrdinalIgnoreCase);

        var user = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(user.PasswordHash);
        Assert.Equal(UserRole.Operator, user.Role);
        Assert.Null(user.AccessLinkHash);
        var link = Assert.Single(await LinksOfAsync(invited.User.Id));
        Assert.Equal("https://accounts.google.com/", link.Issuer);
        Assert.Equal(account.Subject, link.Subject);

        // O link já não vale, e o usuário aparece com o Google como forma de entrada.
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.LookupInvitationAsync(GoogleFlow.Client(fixture), invited.Token)).StatusCode);
        var detail = await TwoFactorTestClient.SendAsync(GoogleFlow.Client(fixture), HttpMethod.Get, $"/users/{invited.User.Id}", caller.AccessToken);
        Assert.Equal(["google"], (await TwoFactorTestClient.JsonAsync(detail)).GetProperty("formasDeEntrada").EnumerateArray().Select(form => form.GetString()));
    }

    [Fact]
    public async Task TheAcceptCookie_DoesNotIssueACode_AndTheStepIsAlwaysTheEnrollment()
    {
        var (_, invited) = await InvitedAsync("aceite-cookie");
        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));
        Assert.Equal("cadastro", returned.Query["segundoPasso"]);
        var pending = returned.Cookie(Session);

        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(GoogleFlow.Client(fixture), IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), pending);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var (second, _) = await TwoFactorTestClient.SecondFactorAsync(GoogleFlow.Client(fixture), pending, "123456");
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Contains("A entrada expirou. Informe e-mail e senha de novo.", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accepting_ClearsTheEmailLock_SoTheEnrollmentIsNot423()
    {
        var (_, invited) = await InvitedAsync("aceite-bloqueado");
        var browser = GoogleFlow.Client(fixture);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LocalUserSeed.PostSessionAsync(browser, invited.User.Email, "tentativa-errada");
        }

        Assert.Equal((HttpStatusCode)423, (await LocalUserSeed.PostSessionAsync(browser, invited.User.Email, "tentativa-errada")).StatusCode);

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));
        var (setup, _) = await TwoFactorTestClient.EnrollmentAsync(browser, "setup", returned.Cookie(Session));

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
    }

    [Fact]
    public async Task TheAcceptedByTheProvider_HasNoPassword_ForTheLogin_NorForTheInvitationLink()
    {
        var (_, invited) = await InvitedAsync("aceite-sem-senha", UserRole.Operator);
        await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email));
        var browser = GoogleFlow.Client(fixture);

        var login = await LocalUserSeed.PostSessionAsync(browser, invited.User.Email, "qualquer-senha-de-teste");
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.DoesNotContain(GoogleFlow.SetCookiesOf(login), cookie => cookie.StartsWith($"{Session}=", StringComparison.Ordinal));

        var accept = await AccessLinkClient.AcceptAsync(browser, invited.Token, "senha-depois-do-google");
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Null((await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id)).PasswordHash);
    }

    // ---- As recusas ----

    [Fact]
    public async Task AnotherEmail_GoesBackToTheInvitation_WithTheConfirmedEmailInTheRefusalCookie()
    {
        var (_, invited) = await InvitedAsync("outro-email");
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("pessoal"));

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, account);

        await AssertRefusedWithoutSessionAsync(returned, "outro-email", invited, stillPending: true);
        Assert.DoesNotContain(account.Email!, returned.Location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invited.User.Email, returned.Location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invited.Token, returned.Location, StringComparison.Ordinal);
        Assert.DoesNotContain(WebEncoders.Base64UrlEncode(AccessLinkToken.Hash(invited.Token)), returned.Location, StringComparison.Ordinal);

        // O painel lê o aviso a partir de /convite: o e-mail que o provedor confirmou, uma vez só.
        var browser = GoogleFlow.Client(fixture);
        var cookie = returned.Cookie(Buteco.Api.Identity.Federated.FederatedLogin.RefusalCookieName);
        var first = await ReadRefusalAsync(browser, cookie);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("outro-email", body.GetProperty("motivo").GetString());
        Assert.Equal("corporativo", body.GetProperty("provedor").GetString());
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await ReadRefusalAsync(browser, cookie)).StatusCode);

        // O convite continua aceito por senha.
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(browser, invited.Token, "senha-depois-do-outro-email")).StatusCode);
    }

    [Fact]
    public async Task AnUnverifiedEmail_IsRefused_AndTheInvitationStaysPending()
    {
        var (_, invited) = await InvitedAsync("nao-verificado");

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email, verified: false));

        await AssertRefusedWithoutSessionAsync(returned, "email-nao-verificado", invited, stillPending: true);
        Assert.Null(returned.Cookie(Buteco.Api.Identity.Federated.FederatedLogin.RefusalCookieName));
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(GoogleFlow.Client(fixture), invited.Token, "senha-depois-do-nao-verificado")).StatusCode);
    }

    [Fact]
    public async Task ADeadInvitation_ComesBeforeTheEmail()
    {
        var (caller, invited) = await InvitedAsync("morto-antes");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartInvitationAsync(client, invited.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(client, caller, invited.User.Id)).StatusCode);

        var returned = await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("outra-conta"))), started.Query["state"], started.FlowCookie);

        Assert.Equal("convite-invalido", returned.Query.GetValueOrDefault("recusa"));
        Assert.StartsWith("/convite?", returned.Location, StringComparison.Ordinal);
        Assert.Null(returned.Cookie(Buteco.Api.Identity.Federated.FederatedLogin.RefusalCookieName));
        Assert.Null(returned.Cookie(Session));
    }

    public static TheoryData<string> DeadInTheMiddle => ["vencido", "cancelado", "aceito-por-senha", "link-novo"];

    [Theory]
    [MemberData(nameof(DeadInTheMiddle))]
    public async Task AnInvitationThatDiedInTheMiddle_IsInvalid_WithoutLink(string kind)
    {
        var (caller, invited) = await InvitedAsync($"meio-{kind}");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartInvitationAsync(client, invited.Token);
        string? newToken = null;
        switch (kind)
        {
            case "vencido":
                await ExpireAsync(invited.User.Id);
                break;
            case "cancelado":
                Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(client, caller, invited.User.Id)).StatusCode);
                break;
            case "aceito-por-senha":
                Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(client, invited.Token, "senha-no-meio-do-fluxo")).StatusCode);
                break;
            default:
                newToken = await AccessLinkClient.TokenAsync(await AccessLinkClient.RenewAsync(client, caller, invited.User.Id, caller.Password));
                break;
        }

        var returned = await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(invited.User.Email)), started.Query["state"], started.FlowCookie);

        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.StartsWith("/convite?", returned.Location, StringComparison.Ordinal);
        Assert.Equal("convite-invalido", returned.Query.GetValueOrDefault("recusa"));
        Assert.Null(returned.Cookie(Session));
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        if (newToken is not null)
        {
            Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.LookupInvitationAsync(client, newToken)).StatusCode);
        }
    }

    [Fact]
    public async Task AProviderAccountOfAnotherUser_IsInUse_AndNoLinkChanges()
    {
        var (_, invited) = await InvitedAsync("em-uso");
        var other = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("dono-do-par"), "senha-do-dono-do-par");
        var account = GoogleFlow.NewAccount(invited.User.Email);
        await GoogleFlow.LinkAsync(fixture.Services, other.Id, account);

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, account);

        await AssertRefusedWithoutSessionAsync(returned, "em-uso", invited, stillPending: true);
        Assert.Empty(await LinksOfAsync(invited.User.Id));
        var kept = Assert.Single(await LinksOfAsync(other.Id));
        Assert.Equal(account.Subject, kept.Subject);
    }

    [Fact]
    public async Task TheCancelAtTheProvider_IsFailure_BackToTheInvitation()
    {
        var (_, invited) = await InvitedAsync("cancelou");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartInvitationAsync(client, invited.Token);

        var returned = await GoogleFlow.ReturnAsync(client, code: null, started.Query["state"], started.FlowCookie, error: "access_denied");

        await AssertRefusedWithoutSessionAsync(returned, "falha", invited, stillPending: true);
    }

    [Fact]
    public async Task AnUnreadableState_IsFailure_BackToTheLogin()
    {
        var returned = await GoogleFlow.ReturnAsync(GoogleFlow.Client(fixture), "codigo-qualquer", "state-forjado", flowCookie: null);

        Assert.Equal("/entrar?recusa=falha", returned.Location);
        Assert.Null(returned.Cookie(Session));
    }

    [Fact]
    public async Task NoRefusal_OfTheAccept_WritesASession()
    {
        var outcomes = new List<(string Expected, GoogleFlow.Returned Returned, InvitationSeed.Seeded Invited, bool Pending)>();

        var (_, a) = await InvitedAsync("sem-sessao-outro");
        outcomes.Add(("outro-email", await GoogleFlow.AcceptInvitationAsync(fixture, a.Token, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("x"))), a, true));
        var (_, b) = await InvitedAsync("sem-sessao-verif");
        outcomes.Add(("email-nao-verificado", await GoogleFlow.AcceptInvitationAsync(fixture, b.Token, GoogleFlow.NewAccount(b.User.Email, verified: false)), b, true));
        var (callerC, c) = await InvitedAsync("sem-sessao-morto");
        var clientC = GoogleFlow.Client(fixture);
        var startedC = await GoogleFlow.StartInvitationAsync(clientC, c.Token);
        await AccessLinkClient.CancelAsync(clientC, callerC, c.User.Id);
        outcomes.Add(("convite-invalido", await GoogleFlow.ReturnAsync(clientC, GoogleFlow.Consent(fixture, startedC, GoogleFlow.NewAccount(c.User.Email)), startedC.Query["state"], startedC.FlowCookie), c, false));
        var (_, d) = await InvitedAsync("sem-sessao-uso");
        var accountD = GoogleFlow.NewAccount(d.User.Email);
        var owner = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("dono"), "senha-do-dono-123");
        await GoogleFlow.LinkAsync(fixture.Services, owner.Id, accountD);
        outcomes.Add(("em-uso", await GoogleFlow.AcceptInvitationAsync(fixture, d.Token, accountD), d, true));
        var (_, e) = await InvitedAsync("sem-sessao-falha");
        var clientE = GoogleFlow.Client(fixture);
        var startedE = await GoogleFlow.StartInvitationAsync(clientE, e.Token);
        outcomes.Add(("falha", await GoogleFlow.ReturnAsync(clientE, null, startedE.Query["state"], startedE.FlowCookie, error: "access_denied"), e, true));

        foreach (var (expected, returned, invited, pending) in outcomes)
        {
            await AssertRefusedWithoutSessionAsync(returned, expected, invited, pending);
        }

        // O sexto motivo, dominio-nao-permitido, pede o HostedDomain: está no FederatedInvitationHostedDomainTests.
    }

    [Fact]
    public async Task TheAcceptLog_CarriesNeitherEmailNorSubject()
    {
        var before = fixture.Logs.Lines.Count;
        var secrets = new List<string>();

        var (_, ok) = await InvitedAsync("log-aceite");
        var okAccount = GoogleFlow.NewAccount(ok.User.Email);
        await GoogleFlow.AcceptInvitationAsync(fixture, ok.Token, okAccount);
        secrets.AddRange([ok.User.Email, okAccount.Subject, ok.Token, WebEncoders.Base64UrlEncode(AccessLinkToken.Hash(ok.Token))]);

        var (_, other) = await InvitedAsync("log-outro");
        var otherAccount = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("log-pessoal"));
        await GoogleFlow.AcceptInvitationAsync(fixture, other.Token, otherAccount);
        secrets.AddRange([other.User.Email, otherAccount.Email!, otherAccount.Subject, other.Token]);

        var (_, unverified) = await InvitedAsync("log-verif");
        var unverifiedAccount = GoogleFlow.NewAccount(unverified.User.Email, verified: false);
        await GoogleFlow.AcceptInvitationAsync(fixture, unverified.Token, unverifiedAccount);
        secrets.AddRange([unverified.User.Email, unverifiedAccount.Subject]);

        var (_, inUse) = await InvitedAsync("log-uso");
        var inUseAccount = GoogleFlow.NewAccount(inUse.User.Email);
        var owner = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("log-dono"), "senha-do-dono-123");
        await GoogleFlow.LinkAsync(fixture.Services, owner.Id, inUseAccount);
        await GoogleFlow.AcceptInvitationAsync(fixture, inUse.Token, inUseAccount);
        secrets.AddRange([inUse.User.Email, inUseAccount.Subject, owner.Id.ToString()]);

        var lines = fixture.Logs.Lines.Skip(before).ToList();
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(lines, line => line.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(lines, line => line.EventId.Id == 1203 && line.Message.Contains(ok.User.Id.ToString(), StringComparison.Ordinal));
        foreach (var reason in new[] { "outro-email", "email-nao-verificado", "em-uso" })
        {
            Assert.Contains(lines, line => line.EventId.Id == 1202 && line.Message.Contains(reason, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheRefusal_GoesBackToTheInvitation_NotToTheLogin()
    {
        var (_, invited) = await InvitedAsync("volta-convite");

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("outra")));

        Assert.Equal("/convite?recusa=outro-email&provedor=corporativo", returned.Location);
    }

    // ---- Apoio ----

    private async Task<(SignedInUser Caller, InvitationSeed.Seeded Invited)> InvitedAsync(string prefix, UserRole role = UserRole.Admin)
    {
        var caller = await SignedInUser.CreateAsync(fixture, prefix);
        return (caller, await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id, role: role));
    }

    private async Task<List<Buteco.Api.Users.Federated.UserFederatedIdentity>> LinksOfAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserFederatedIdentities.AsNoTracking().Where(link => link.UserId == userId).ToListAsync();
    }

    private async Task ExpireAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var past = fixture.Clock.GetUtcNow().AddMinutes(-1);
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.AccessLinkExpiresAt, past));
    }

    private static Task<HttpResponseMessage> ReadRefusalAsync(HttpClient client, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/federated/refusal") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return client.SendAsync(request);
    }

    private async Task AssertRefusedWithoutSessionAsync(GoogleFlow.Returned returned, string reason, InvitationSeed.Seeded invited, bool stillPending)
    {
        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.StartsWith("/convite?", returned.Location, StringComparison.Ordinal);
        Assert.Equal(reason, returned.Query.GetValueOrDefault("recusa"));
        Assert.Equal("corporativo", returned.Query.GetValueOrDefault("provedor"));
        Assert.Null(returned.Cookie(Session));
        Assert.Empty(await LinksOfAsync(invited.User.Id));

        if (stillPending)
        {
            var reloaded = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
            Assert.Equal(UserStatus.Invited, reloaded.Status);
            Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
        }

        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var cookies = string.Join("; ", returned.SetCookies.Select(header => header.Split(';')[0]).Where(pair => !pair.EndsWith('=')));
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(GoogleFlow.Client(fixture),
            IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookies.Length == 0 ? null : cookies);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}

/// <summary>O aceite com <c>HostedDomain</c> <c>empresa.com</c> (o sexto motivo, <c>dominio-nao-permitido</c>).</summary>
public class FederatedInvitationHostedDomainTests(HostedDomainFederatedLoginFixture fixture) : IClassFixture<HostedDomainFederatedLoginFixture>
{
    [Fact]
    public async Task AnotherDomain_IsRefused_WithoutSession_AndTheInvitationStaysPending()
    {
        var caller = await SignedInUser.CreateAsync(fixture, "dominio-aceite");
        var invited = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);

        var returned = await GoogleFlow.AcceptInvitationAsync(fixture, invited.Token, GoogleFlow.NewAccount(invited.User.Email) with { HostedDomain = "outra.com" });

        Assert.Equal("/convite?recusa=dominio-nao-permitido&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        var reloaded = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
    }
}
