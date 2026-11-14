using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O login federado contra o duplo do Google (specs <c>federated-login</c> e <c>issuer-user-login</c>,
/// "A sessão pendente também nasce do retorno do provedor federado"; design.md da change
/// provedores-federados-google, D1, D4, D5 e D7). Guardas verificados contra o defeito (tasks.md, 3.5 e 5.6
/// a 5.16).
/// </summary>
public class FederatedLoginTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    private const string Session = "buteco-sessao";

    private static readonly string Authorize = "/connect/authorize?client_id=buteco-painel&response_type=code&state=s";

    // ---- O início ----

    [Fact]
    public async Task Start_ReturnsTheGoogleUrl_AndTheFlowCookie()
    {
        var started = await GoogleFlow.StartAsync(GoogleFlow.Client(fixture), Authorize);

        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", started.Url, StringComparison.Ordinal);
        var query = started.Query;
        Assert.Equal(FakeGoogle.ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);

        // query, o padrão do code (o OpenIddict só anexa response_mode quando não é o padrão); nunca form_post,
        // que exigiria SameSite=None no cookie do fluxo.
        Assert.Equal("query", query.GetValueOrDefault("response_mode") ?? "query");
        Assert.Equal(new[] { "email", "openid", "profile" }, query["scope"].Split(' ').Order(StringComparer.Ordinal));
        // A base do painel (Identity:PanelBaseUrl, ou a url pública), como os retornos do buteco-painel.
        Assert.Equal(fixture.Services.GetRequiredService<Buteco.Api.Identity.Federated.FederatedRedirect>().RedirectUri.AbsoluteUri, query["redirect_uri"]);
        Assert.EndsWith("/auth/federated/callback", query["redirect_uri"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.False(string.IsNullOrEmpty(query["state"]));

        // O cookie do fluxo (D4): Lax, só no caminho do retorno, 10 minutos, com só o valor anti-falsificação.
        var header = GoogleFlow.SetCookiesOf(started.Response).Single(cookie => cookie.StartsWith("buteco-fluxo-externo.", StringComparison.Ordinal));
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth/federated/callback", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        var expires = DateTimeOffset.Parse(header.Split(';').Select(part => part.Trim()).Single(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..]);
        Assert.InRange(expires - fixture.Clock.GetUtcNow(), TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10.1));
        var value = started.FlowCookie!.Split('=', 2)[1];
        Assert.DoesNotContain("connect", value, StringComparison.OrdinalIgnoreCase);
        Assert.True(value.Length < 200, "o cookie do fluxo deveria levar só o valor anti-falsificação");
    }

    [Fact]
    public async Task Start_FromAnotherOrigin_IsRejected_WithoutCookieNorUrl()
    {
        var started = await GoogleFlow.StartAsync(GoogleFlow.Client(fixture), origin: "https://outro-site.exemplo");

        Assert.NotEqual(HttpStatusCode.OK, started.Response.StatusCode);
        Assert.Null(started.Url);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
    }

    [Fact]
    public async Task Start_WithAnUnknownKey_IsNotFound()
    {
        var started = await GoogleFlow.StartAsync(GoogleFlow.Client(fixture), key: "inexistente");

        Assert.Equal(HttpStatusCode.NotFound, started.Response.StatusCode);
        Assert.Contains("Provedor não configurado.", await started.Response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
    }

    [Fact]
    public async Task Start_DiscardsAReturnThatIsNotAnAuthorize()
    {
        var (user, account) = await LinkedUserAsync("retorno-descartado");
        await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, user.Id);

        var returned = await GoogleFlow.LoginAsync(fixture, account, retorno: "https://malicioso.exemplo/connect/authorize?x=1");

        Assert.StartsWith("/entrar?", returned.Location, StringComparison.Ordinal);
        Assert.False(returned.Query.ContainsKey("retorno"));
    }

    // ---- A correlação e o state ----

    [Fact]
    public async Task Return_WithoutTheFlowCookie_IsRefused()
    {
        var (_, account) = await LinkedUserAsync("sem-cookie");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);

        var returned = await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, account), started.Query["state"], flowCookie: null);

        await AssertRefusedWithoutSessionAsync(returned, "falha");
    }

    [Fact]
    public async Task Return_WithTheCookieOfAnotherFlow_IsRefused()
    {
        var (_, account) = await LinkedUserAsync("outro-fluxo");
        var client = GoogleFlow.Client(fixture);
        var first = await GoogleFlow.StartAsync(client);
        var second = await GoogleFlow.StartAsync(client);

        // O state do fluxo A com só o cookie do fluxo B do mesmo navegador.
        var returned = await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, first, account), first.Query["state"], second.FlowCookie);

        await AssertRefusedWithoutSessionAsync(returned, "falha");
    }

    [Fact]
    public async Task Return_Replayed_IsRefused()
    {
        var (user, account) = await LinkedUserAsync("repetido");
        await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, user.Id);
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);
        var code = GoogleFlow.Consent(fixture, started, account);
        var first = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);

        // O mesmo state, outro código válido do mesmo consentimento: só o state usado barra a segunda.
        var again = GoogleFlow.Consent(fixture, started, account);
        var second = await GoogleFlow.ReturnAsync(client, again, started.Query["state"], started.FlowCookie);

        Assert.StartsWith("/entrar?federado=", first.Location, StringComparison.Ordinal);
        await AssertRefusedWithoutSessionAsync(second, "falha");
    }

    [Fact]
    public async Task Return_AfterTheStateExpired_IsRefused()
    {
        var (_, account) = await LinkedUserAsync("vencido");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);
        var code = GoogleFlow.Consent(fixture, started, account);

        fixture.Clock.Advance(TimeSpan.FromMinutes(11));
        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);

        await AssertRefusedWithoutSessionAsync(returned, "falha");
    }

    // ---- A entrada ----

    [Fact]
    public async Task LinkedActive_WithTwoFactor_GetsThePendingSession_AndTheCodeCompletesIt()
    {
        var (user, account) = await LinkedUserAsync("entra-totp");
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, user.Id);
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var authorize = IdentityIssuerUserFlow.AuthorizeUrl(pkce);

        var returned = await GoogleFlow.LoginAsync(fixture, account, retorno: authorize);

        // O retorno manda ao /entrar, com o passo e o retorno, nunca ao authorize (D5).
        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.StartsWith("/entrar?", returned.Location, StringComparison.Ordinal);
        Assert.Equal(FederatedLoginFixture.Key, returned.Query["federado"]);
        Assert.Equal("totp", returned.Query["segundoPasso"]);
        Assert.Equal(authorize, returned.Query["retorno"]);
        var pending = returned.Cookie(Session);
        Assert.NotNull(pending);
        Assert.Contains("samesite=strict", returned.SetCookieHeader(Session)!, StringComparison.OrdinalIgnoreCase);

        // O cookie pendente do retorno não emite código.
        var client = GoogleFlow.Client(fixture);
        var beforeCode = await IdentityIssuerUserFlow.AuthorizeAsync(client, authorize, pending);
        Assert.Equal(HttpStatusCode.Redirect, beforeCode.StatusCode);
        Assert.StartsWith("/entrar", beforeCode.Headers.Location!.OriginalString, StringComparison.Ordinal);

        // O segundo passo de hoje completa a sessão, e o authorize seguinte emite o código.
        var (second, complete) = await TwoFactorTestClient.SecondFactorAsync(client, pending, TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock, stepOffset: 1));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var withCode = await IdentityIssuerUserFlow.AuthorizeAsync(client, authorize, complete);
        var tokens = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RedeemAsync(client, IdentityIssuerUserFlow.CodeFrom(withCode), pkce.Verifier));

        // O token do Buteco não muda com o login federado.
        var access = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        Assert.Equal("https://api.exemplo.test/", access.Issuer);
        Assert.Contains("buteco-api", access.Audiences);
        Assert.Equal("buteco-painel", access.Claims.Single(claim => claim.Type == "client_id").Value);
        Assert.Equal(user.Id.ToString(), access.Subject);
    }

    [Fact]
    public async Task LinkedActive_WithoutTwoFactor_EnrollsInTheLogin()
    {
        var (user, account) = await LinkedUserAsync("entra-cadastro");

        var returned = await GoogleFlow.LoginAsync(fixture, account);

        Assert.Equal("cadastro", returned.Query["segundoPasso"]);
        var enrolled = await TwoFactorTestClient.EnrollAsync(GoogleFlow.Client(fixture), fixture.Services, returned.Cookie(Session)!);
        Assert.Equal(10, enrolled.RecoveryCodes.Count);
        Assert.True((await SignedInUser.ReloadAsync(fixture.Services, user.Id)).IsTwoFactorEnabled);
    }

    // ---- O par decide ----

    [Fact]
    public async Task ThePairDecides_NotTheEmail()
    {
        var (user, _) = await LinkedUserAsync("par-decide");

        // Outro sub, com o e-mail de quem está vinculado, verificado.
        var returned = await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount(user.Email));

        await AssertRefusedWithoutSessionAsync(returned, "nao-conectada");
    }

    [Fact]
    public async Task TheEmailChangedAtTheProvider_EntersAsTheSameUser_AndOnlyTheLinkChanges()
    {
        var (user, account) = await LinkedUserAsync("email-mudou");
        var changed = account with { Email = LocalUserSeed.UniqueEmail("email-novo") };

        var returned = await GoogleFlow.LoginAsync(fixture, changed);

        Assert.StartsWith("/entrar?federado=", returned.Location, StringComparison.Ordinal);
        Assert.NotNull(returned.Cookie(Session));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.UserFederatedIdentities.AsNoTracking().SingleAsync(candidate => candidate.UserId == user.Id);
        Assert.Equal(changed.Email, link.Email);
        Assert.NotNull(link.LastUsedAt);
        Assert.Equal(user.Email, (await db.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id)).Email);
    }

    // ---- As recusas ----

    [Fact]
    public async Task LinkedDeactivated_IsRefusedAsDeactivated()
    {
        var (user, account) = await LinkedUserAsync("vinculado-desativado");
        await LocalUserSeed.DeactivateAsync(fixture.Services, user.Id);

        await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, account), "desativado");
    }

    [Fact]
    public async Task DeactivatedWithoutALink_IsNoAccess()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("desativado-sem-vinculo"), "senha-de-teste-123");
        await LocalUserSeed.DeactivateAsync(fixture.Services, user.Id);

        await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount(user.Email)), "sem-acesso");
    }

    [Fact]
    public async Task UnverifiedEmailWithoutALink_IsEmailNotVerified()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("nao-verificado"), "senha-de-teste-123");

        await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount(user.Email, verified: false)), "email-nao-verificado");
    }

    [Fact]
    public async Task PendingInvitation_IsRefusedAsPending()
    {
        var inviter = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("quem-convida"), "senha-de-teste-123");
        var invited = await InvitationSeed.InvitedAsync(fixture.Services, inviter.Id);

        await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount(invited.User.Email)), "convite-pendente");
    }

    [Fact]
    public async Task NoUser_IsNoAccess_AndTheLocationHasNoEmail()
    {
        var account = GoogleFlow.NewAccount();

        var returned = await GoogleFlow.LoginAsync(fixture, account);

        await AssertRefusedWithoutSessionAsync(returned, "sem-acesso");
        Assert.DoesNotContain(account.Email!, Uri.UnescapeDataString(returned.Location), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", Uri.UnescapeDataString(returned.Location), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessDeniedAtTheProvider_IsFailure()
    {
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);

        var returned = await GoogleFlow.ReturnAsync(client, code: null, started.Query["state"], started.FlowCookie, error: "access_denied");

        await AssertRefusedWithoutSessionAsync(returned, "falha");
    }

    [Fact]
    public async Task NoRefusal_WritesASession()
    {
        // Os seis motivos deste host; o sétimo (dominio-nao-permitido) está em FederatedLoginHostedDomainTests.
        var inviter = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("convida-recusas"), "senha-de-teste-123");
        var invited = await InvitationSeed.InvitedAsync(fixture.Services, inviter.Id);
        var (deactivated, deactivatedAccount) = await LinkedUserAsync("recusas-desativado");
        await LocalUserSeed.DeactivateAsync(fixture.Services, deactivated.Id);
        var active = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("recusas-ativo"), "senha-de-teste-123");

        var cases = new (FakeGoogle.Account Account, string Reason)[]
        {
            (deactivatedAccount, "desativado"),
            (GoogleFlow.NewAccount(active.Email, verified: false), "email-nao-verificado"),
            (GoogleFlow.NewAccount(invited.User.Email), "convite-pendente"),
            (GoogleFlow.NewAccount(active.Email), "nao-conectada"),
            (GoogleFlow.NewAccount(), "sem-acesso"),
            (GoogleFlow.NewAccount() with { IssuerOverride = "https://outro.exemplo.com" }, "falha"),
        };

        foreach (var (account, reason) in cases)
        {
            await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, account), reason);
        }
    }

    [Theory]
    [InlineData("https://outro.exemplo.com")]
    [InlineData("accounts.google.com")]
    public async Task AnIdTokenWithAnotherIssuer_IsRefused(string issuer)
    {
        var (_, account) = await LinkedUserAsync("iss-errado");

        await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, account with { IssuerOverride = issuer }), "falha");
    }

    [Fact]
    public async Task AnIdTokenForAnotherClient_WithAnotherNonce_OrSignedByAnotherKey_IsRefused()
    {
        var (_, account) = await LinkedUserAsync("id-token-desvios");

        foreach (var deviation in new[]
                 {
                     account with { AudienceOverride = "outro-cliente.apps.googleusercontent.com" },
                     account with { NonceOverride = "outro-nonce" },
                     account with { SignWithAnotherKey = true },
                 })
        {
            await AssertRefusedWithoutSessionAsync(await GoogleFlow.LoginAsync(fixture, deviation), "falha");
        }
    }

    [Fact]
    public async Task TheReturn_WithTheProviderDown_IsFailure_WithoutSession()
    {
        // O discovery já está no cache (o início passou); a troca do código encontra o provedor fora do ar.
        var (_, account) = await LinkedUserAsync("retorno-fora-do-ar");
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);
        var code = GoogleFlow.Consent(fixture, started, account);

        fixture.Google.State = FakeGoogle.Availability.Down;
        try
        {
            await AssertRefusedWithoutSessionAsync(await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie), "falha");
        }
        finally
        {
            fixture.Google.State = FakeGoogle.Availability.Up;
        }
    }

    // ---- O e-mail confirmado da recusa ----

    [Fact]
    public async Task TheRefusalCookie_IsReadOnce()
    {
        var account = GoogleFlow.NewAccount();
        var returned = await GoogleFlow.LoginAsync(fixture, account);
        var cookie = returned.Cookie("buteco-recusa")!;

        var header = returned.SetCookieHeader("buteco-recusa")!;
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth/federated/refusal", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=120", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.Email!, Uri.UnescapeDataString(cookie), StringComparison.OrdinalIgnoreCase);

        var client = GoogleFlow.Client(fixture);
        var first = await ReadRefusalAsync(client, cookie);
        var replayed = await ReadRefusalAsync(client, cookie);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("sem-acesso", body.GetProperty("motivo").GetString());
        Assert.Equal(FederatedLoginFixture.Key, body.GetProperty("provedor").GetString());
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
        Assert.Contains(GoogleFlow.SetCookiesOf(first), set => set.StartsWith("buteco-recusa=;", StringComparison.Ordinal) && set.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.NotFound, replayed.StatusCode);
        Assert.Empty(await replayed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheRefusalRoute_RevealsNothingWithoutTheCookie()
    {
        var client = GoogleFlow.Client(fixture);
        var expired = (await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount())).Cookie("buteco-recusa")!;
        var fresh = (await GoogleFlow.LoginAsync(fixture, GoogleFlow.NewAccount())).Cookie("buteco-recusa")!;
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));

        var withoutCookie = await ReadRefusalAsync(client, cookie: null);
        var tampered = await ReadRefusalAsync(client, fresh[..^4] + "AAAA");
        var tooOld = await ReadRefusalAsync(client, expired);

        foreach (var response in new[] { withoutCookie, tampered, tooOld })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task TheRefusalRoute_KeepsTheLoginBarriers()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/federated/refusal") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Origin", "https://outro-site.exemplo");

        var response = await GoogleFlow.Client(fixture).SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- O log ----

    [Fact]
    public async Task NoLogLine_CarriesTheEmailOrTheSub()
    {
        var before = fixture.Logs.Lines.Count;
        var (user, linked) = await LinkedUserAsync("log-entra");
        var active = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("log-nao-conectada"), "senha-de-teste-123");
        var notConnected = GoogleFlow.NewAccount(active.Email);
        var noAccess = GoogleFlow.NewAccount();
        var failure = GoogleFlow.NewAccount() with { IssuerOverride = "https://outro.exemplo.com" };

        await GoogleFlow.LoginAsync(fixture, linked);
        await GoogleFlow.LoginAsync(fixture, notConnected);
        await GoogleFlow.LoginAsync(fixture, noAccess);
        await GoogleFlow.LoginAsync(fixture, failure);

        var lines = fixture.Logs.Lines.Skip(before).ToList();
        foreach (var secret in new[] { user.Email, linked.Subject, active.Email, notConnected.Subject, noAccess.Email!, noAccess.Subject, failure.Subject })
        {
            Assert.DoesNotContain(lines, line => line.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Contains(lines, line => line.EventId.Id == 1198 && line.Message.Contains("nao-conectada", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EventId.Id == 1198 && line.Message.Contains("sem-acesso", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EventId.Id == 1199);
    }

    // ---- O state token no banco ----

    [Fact]
    public async Task TheStart_WritesAStateToken_AndTheReturnRedeemsIt_WithoutASession()
    {
        var (user, account) = await LinkedUserAsync("state-token");
        await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, user.Id);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().AsNoTracking();
        const string stateType = "urn:openiddict:params:oauth:token-type:state_token";
        var before = await tokens.Where(token => token.Type == stateType).Select(token => token.Id).ToListAsync();

        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);

        var created = await tokens.Where(token => token.Type == stateType && !before.Contains(token.Id)).ToListAsync();
        var mine = Assert.Single(created);
        Assert.Equal("valid", mine.Status);
        Assert.Null(mine.Authorization);

        await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, account), started.Query["state"], started.FlowCookie);

        db.ChangeTracker.Clear();
        var redeemed = await db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().AsNoTracking().SingleAsync(token => token.Id == mine.Id);
        Assert.Equal("redeemed", redeemed.Status);
        Assert.Empty(await fixture.Services.GetRequiredService<IServiceScopeFactory>().CreateScope().ServiceProvider
            .GetRequiredService<Buteco.Api.Users.Sessions.UserSessions>().ListLiveAsync(user.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ThePrune_RemovesADeadStateToken()
    {
        var (_, account) = await LinkedUserAsync("state-limpeza");
        var client = GoogleFlow.Client(fixture);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>();
        const string stateType = "urn:openiddict:params:oauth:token-type:state_token";
        var before = await tokens.AsNoTracking().Where(token => token.Type == stateType).Select(token => token.Id).ToListAsync();
        var started = await GoogleFlow.StartAsync(client);
        await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, account), started.Query["state"], started.FlowCookie);
        var mine = await tokens.AsNoTracking().Where(token => token.Type == stateType && !before.Contains(token.Id)).Select(token => token.Id).SingleAsync();

        // Resgatado há mais de 14 dias: a mesma regra das outras entradas mortas.
        var old = fixture.Clock.GetUtcNow().UtcDateTime.AddDays(-15);
        await tokens.Where(token => token.Id == mine)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.CreationDate, old).SetProperty(token => token.ExpirationDate, old));
        await fixture.Services.GetRequiredService<Buteco.Api.Identity.Storage.IIdentityTokenPruner>().RunCycleAsync(CancellationToken.None);

        Assert.False(await tokens.AsNoTracking().AnyAsync(token => token.Id == mine));
    }

    // ---- Apoio ----

    private async Task<(User User, FakeGoogle.Account Account)> LinkedUserAsync(string prefix)
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail(prefix), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(user.Email);
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);
        return (user, account);
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

    /// <summary>
    /// A recusa: o <c>302</c> ao <c>/entrar</c> com o motivo, nenhum <c>buteco-sessao</c>, e um authorize com
    /// os cookies que o retorno deixou responde como sem sessão.
    /// </summary>
    private async Task AssertRefusedWithoutSessionAsync(GoogleFlow.Returned returned, string reason)
    {
        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.StartsWith("/entrar?", returned.Location, StringComparison.Ordinal);
        Assert.Equal(reason, returned.Query.GetValueOrDefault("recusa"));
        Assert.Null(returned.Cookie(Session));

        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var cookies = string.Join("; ", returned.SetCookies.Select(header => header.Split(';')[0]).Where(pair => !pair.EndsWith('=')));
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(GoogleFlow.Client(fixture),
            IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookies.Length == 0 ? null : cookies);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/entrar", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}

/// <summary>O <c>HostedDomain</c> (spec <c>federated-login</c>, "Decisão do login no retorno", passo 1). Guarda 5.14.</summary>
public class FederatedLoginHostedDomainTests(HostedDomainFederatedLoginFixture fixture) : IClassFixture<HostedDomainFederatedLoginFixture>
{
    [Fact]
    public async Task AnotherDomain_IsRefused_EvenWhenLinked()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("hd-outro"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(user.Email) with { HostedDomain = "outra.com" };
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);

        var returned = await GoogleFlow.LoginAsync(fixture, account);

        Assert.Equal("dominio-nao-permitido", returned.Query.GetValueOrDefault("recusa"));
        Assert.Null(returned.Cookie("buteco-sessao"));
    }

    [Fact]
    public async Task TheConfiguredDomain_Enters()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("hd-certo"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(user.Email) with { HostedDomain = "empresa.com" };
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);

        var returned = await GoogleFlow.LoginAsync(fixture, account);

        Assert.StartsWith("/entrar?federado=", returned.Location, StringComparison.Ordinal);
        Assert.NotNull(returned.Cookie("buteco-sessao"));
    }

    [Fact]
    public async Task WithoutTheClaim_IsRefused()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("hd-ausente"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(user.Email);
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);

        Assert.Equal("dominio-nao-permitido", (await GoogleFlow.LoginAsync(fixture, account)).Query.GetValueOrDefault("recusa"));
    }
}
