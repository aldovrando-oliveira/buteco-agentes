using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.Federated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// Conectar e desconectar o provedor, a última forma de entrada, o stamp e as formas de entrada (spec
/// <c>federated-login</c>, <c>user-management</c> e <c>user-account</c>; design.md da change
/// provedores-federados-google, D6 e D9). Guardas verificados contra o defeito (tasks.md, 7.4 a 7.9 e 8.7).
/// </summary>
public class FederatedConnectTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    private const string StaleIssuer = "https://sso.antigo.exemplo/";

    // ---- Conectar ----

    [Fact]
    public async Task Connect_WritesTheLink_WithoutTouchingTheStampOrTheSessions()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta");
        var stamp = (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp;
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("conta-google-qualquer"));

        var returned = await ConnectAsync(person, account, new { senha = person.Password });

        Assert.Equal("/account?conectado=corporativo", returned.Location);
        var link = Assert.Single(await LinksAsync(person.User.Id));
        Assert.Equal(account.Subject, link.Subject);
        Assert.Equal("https://accounts.google.com/", link.Issuer);
        Assert.Equal(stamp, (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp);
        Assert.True(await SignedInUser.RenewsAsync(fixture, person.RefreshToken));
        Assert.Contains(fixture.Logs.Lines, line => line.EventId.Id == 1200 && line.Message.Contains(person.User.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connect_WithoutTheConfirmation_IsRefused_AndCounts()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta-sem-senha");

        var response = await StartConnectAsync(person, new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Senha inválida.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(response));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.LoginThrottles.Where(row => row.NormalizedEmail == person.User.NormalizedEmail).Select(row => row.ConsecutiveFailures).SingleAsync());
    }

    [Fact]
    public async Task Connect_WithAnUnknownKey_IsNotFound()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta-chave");

        var response = await StartConnectAsync(person, new { senha = person.Password }, key: "inexistente");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Connect_AnAccountOfAnotherUser_IsInUse()
    {
        var other = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("dono-da-conta"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(other.Email);
        await GoogleFlow.LinkAsync(fixture.Services, other.Id, account);
        var person = await SignedInUser.CreateAsync(fixture, "conecta-em-uso");

        var returned = await ConnectAsync(person, account, new { senha = person.Password });

        Assert.Equal("/account?recusa=em-uso&provedor=corporativo", returned.Location);
        Assert.Empty(await LinksAsync(person.User.Id));
        Assert.Equal(other.Id, Assert.Single(await LinksBySubjectAsync(account.Subject)).UserId);
    }

    [Fact]
    public async Task Connect_WhenAlreadyConnected_IsRefused()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta-ja");
        await GoogleFlow.LinkAsync(fixture.Services, person.User.Id, GoogleFlow.NewAccount());

        var returned = await ConnectAsync(person, GoogleFlow.NewAccount(), new { senha = person.Password });

        Assert.Equal("/account?recusa=ja-conectado&provedor=corporativo", returned.Location);
        Assert.Single(await LinksAsync(person.User.Id));
    }

    [Fact]
    public async Task Connect_WithTheStampChangedInTheMiddle_IsSessionExpired()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conecta-stamp");
        var client = GoogleFlow.Client(fixture);
        var started = GoogleFlow.Read(await StartConnectAsync(person, new { senha = person.Password }, client: client));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == person.User.Id);
            user.RenewSecurityStamp();
            await db.SaveChangesAsync();
        }

        var returned = await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount()), started.Query["state"], started.FlowCookie);

        Assert.Equal("/account?recusa=sessao-expirada&provedor=corporativo", returned.Location);
        Assert.Empty(await LinksAsync(person.User.Id));
    }

    // ---- Desconectar ----

    [Fact]
    public async Task OnlyTheProvider_DoesNotLeave()
    {
        var (person, _) = await PasswordlessLinkedAsync("so-provedor");

        var response = await ClientFor(person.Id).DeleteAsync("/account/federated/corporativo");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("A última forma de entrada não pode ser removida.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(await LinksAsync(person.Id));
    }

    [Fact]
    public async Task ALinkOfAProviderOutOfTheConfiguration_DoesNotCount()
    {
        var (person, _) = await PasswordlessLinkedAsync("vinculo-antigo-nao-conta");
        await AddStaleLinkAsync(person.Id);

        var response = await ClientFor(person.Id).DeleteAsync("/account/federated/corporativo");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, (await LinksAsync(person.Id)).Count);
    }

    [Fact]
    public async Task WithAPassword_TheProviderLeaves_AndOwnSessionsStay()
    {
        var person = await SignedInUser.CreateAsync(fixture, "desconecta-propria");
        await GoogleFlow.LinkAsync(fixture.Services, person.User.Id, GoogleFlow.NewAccount());
        var stamp = (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp;

        var response = await BearerClient(person.AccessToken).DeleteAsync("/account/federated/corporativo");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await LinksAsync(person.User.Id));
        Assert.Equal(stamp, (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp);
        Assert.True(await SignedInUser.RenewsAsync(fixture, person.RefreshToken));
        Assert.Contains(fixture.Logs.Lines, line => line.EventId.Id == 1201 && line.Message.Contains(person.User.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCheck_LocksTheUserRow_BeforeTheDelete()
    {
        var person = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("trava-linha"), "senha-de-teste-123");
        await GoogleFlow.LinkAsync(fixture.Services, person.Id, GoogleFlow.NewAccount());
        var client = ClientFor(person.Id);

        var commands = (await fixture.SqlCapture.CaptureAsync(async () =>
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/account/federated/corporativo")).StatusCode))).ToList();

        var forUpdate = commands.FindIndex(command => command.Contains("FROM users", StringComparison.Ordinal) && command.Contains("FOR UPDATE", StringComparison.Ordinal));
        var delete = commands.FindIndex(command => command.Contains("DELETE FROM user_federated_identities", StringComparison.Ordinal));
        Assert.True(forUpdate >= 0, "a consulta não pediu o FOR UPDATE da linha do usuário");
        Assert.True(delete > forUpdate, "o DELETE do vínculo veio antes da trava");
    }

    [Fact]
    public async Task TheAdminDisconnects_AndTheSessionsFall()
    {
        var person = await SignedInUser.CreateAsync(fixture, "admin-desconecta");
        await GoogleFlow.LinkAsync(fixture.Services, person.User.Id, GoogleFlow.NewAccount());
        var stamp = (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp;
        var admin = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("admin-que-desconecta"), "senha-de-teste-123");

        var response = await ClientFor(admin.Id).DeleteAsync($"/users/{person.User.Id}/federated/corporativo");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await LinksAsync(person.User.Id));
        Assert.NotEqual(stamp, (await SignedInUser.ReloadAsync(fixture.Services, person.User.Id)).SecurityStamp);
        Assert.False(await SignedInUser.RenewsAsync(fixture, person.RefreshToken));
    }

    [Fact]
    public async Task TheAdmin_DoesNotDisconnectHimselfHere()
    {
        var admin = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("admin-proprio"), "senha-de-teste-123");
        await GoogleFlow.LinkAsync(fixture.Services, admin.Id, GoogleFlow.NewAccount());

        var response = await ClientFor(admin.Id).DeleteAsync($"/users/{admin.Id}/federated/corporativo");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Para desconectar a sua própria conta, use Minha conta.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(await LinksAsync(admin.Id));
    }

    [Fact]
    public async Task WithoutALink_IsNotFound()
    {
        var person = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("sem-vinculo"), "senha-de-teste-123");

        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(person.Id).DeleteAsync("/account/federated/corporativo")).StatusCode);
    }

    // ---- As formas de entrada ----

    [Fact]
    public async Task TheSignInForms_AreStrings_InTheListTheDetailAndTheAccount()
    {
        var withPassword = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("formas-senha-google"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("formas-conta"));
        await GoogleFlow.LinkAsync(fixture.Services, withPassword.Id, account);
        var (passwordless, _) = await PasswordlessLinkedAsync("formas-so-google");
        var admin = ClientFor(withPassword.Id);

        var list = await admin.GetFromJsonAsync<JsonElement>("/users");
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/users/{withPassword.Id}");
        var own = await admin.GetFromJsonAsync<JsonElement>("/account");

        Assert.Equal(["senha", "google"], FormsOf(list.EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == withPassword.Id)));
        Assert.Equal(["google"], FormsOf(list.EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == passwordless.Id)));
        Assert.Equal(["senha", "google"], FormsOf(detail));
        Assert.Equal(["senha", "google"], FormsOf(own));
        foreach (var body in new[] { detail, own })
        {
            var connected = Assert.Single(body.GetProperty("contasConectadas").EnumerateArray());
            Assert.Equal("corporativo", connected.GetProperty("chave").GetString());
            Assert.Equal("google", connected.GetProperty("tipo").GetString());
            Assert.Equal(account.Email, connected.GetProperty("email").GetString());
            Assert.True(connected.GetProperty("disponivel").GetBoolean());
            Assert.DoesNotContain(account.Subject, body.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ALinkOfAProviderOutOfTheConfiguration_ShowsAsUnavailable()
    {
        var person = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("provedor-removido"), "senha-de-teste-123");
        await AddStaleLinkAsync(person.Id);

        var detail = await ClientFor(person.Id).GetFromJsonAsync<JsonElement>($"/users/{person.Id}");

        Assert.Equal(["senha"], FormsOf(detail));
        var connected = Assert.Single(detail.GetProperty("contasConectadas").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, connected.GetProperty("chave").ValueKind);
        Assert.False(connected.GetProperty("disponivel").GetBoolean());
    }

    // ---- Apoio ----

    private static string[] FormsOf(JsonElement body) =>
        [.. body.GetProperty("formasDeEntrada").EnumerateArray().Select(form => form.GetString()!)];

    private async Task<GoogleFlow.Returned> ConnectAsync(SignedInUser person, FakeGoogle.Account account, object body)
    {
        var client = GoogleFlow.Client(fixture);
        var started = GoogleFlow.Read(await StartConnectAsync(person, body, client: client));
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        return await GoogleFlow.ReturnAsync(client, GoogleFlow.Consent(fixture, started, account), started.Query["state"], started.FlowCookie);
    }

    private Task<HttpResponseMessage> StartConnectAsync(SignedInUser person, object body, string key = FederatedLoginFixture.Key, HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/account/federated/{key}/connect") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);
        return (client ?? GoogleFlow.Client(fixture)).SendAsync(request);
    }

    private async Task<(User User, FakeGoogle.Account Account)> PasswordlessLinkedAsync(string prefix)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = User.Create(LocalUserSeed.UniqueEmail(prefix), "Pessoa Sem Senha", passwordHash: null, fixture.Clock.GetUtcNow(), UserRole.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var account = GoogleFlow.NewAccount(user.Email);
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);
        return (user, account);
    }

    private async Task AddStaleLinkAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserFederatedIdentities.Add(UserFederatedIdentity.Connect(userId, StaleIssuer, $"antigo-{Guid.NewGuid():N}", "antigo@exemplo.test", fixture.Clock.GetUtcNow()));
        await db.SaveChangesAsync();
    }

    private async Task<List<UserFederatedIdentity>> LinksAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserFederatedIdentities.AsNoTracking().Where(link => link.UserId == userId).ToListAsync();
    }

    private async Task<List<UserFederatedIdentity>> LinksBySubjectAsync(string subject)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserFederatedIdentities.AsNoTracking().Where(link => link.Subject == subject).ToListAsync();
    }

    private HttpClient ClientFor(Guid userId) =>
        BearerClient(TestIssuerTokens.ForClient(fixture.Services, "buteco-painel", userId.ToString(), "admin"));

    private HttpClient BearerClient(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
