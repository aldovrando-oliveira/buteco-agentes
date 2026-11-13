using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Buteco.Api.Users.Passwords;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// A confirmação de quem chama com o código do app de quem não tem senha (specs <c>user-access-links</c>,
/// <c>user-management</c>, <c>external-applications</c> e <c>local-users</c>; design.md da change
/// provedores-federados-google, D8). Na 189a nenhuma rota cria o usuário ativo sem senha (é o aceite pelo
/// Google, #274): os casos o montam direto no banco. Guardas verificados contra o defeito (tasks.md, 6.4 a 6.7).
/// </summary>
public class CallerConfirmationTests(LocalUsersFixture fixture) : IClassFixture<LocalUsersFixture>
{
    [Fact]
    public async Task WhoHasAPassword_DoesNotConfirmWithTheCode()
    {
        var admin = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("com-senha-codigo"), "senha-de-teste-123");
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, admin.Id);
        var invited = LocalUserSeed.UniqueEmail("convidado-por-codigo");

        var response = await ClientFor(admin).PostAsJsonAsync("/users/invitations",
            new { nome = "Pessoa", email = invited, papel = "leitor", codigo = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(CallerPassword.InvalidMessage, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await FailuresAsync(admin.NormalizedEmail));
        Assert.False(await ExistsAsync(invited));
    }

    [Fact]
    public async Task AnAdminWithAPassword_DoesNotGenerateASecretWithTheCode()
    {
        var admin = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("segredo-codigo"), "senha-de-teste-123");
        var enabled = await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, admin.Id);
        var client = ClientFor(admin);
        var created = await client.PostAsJsonAsync("/applications/", new { nome = $"App {Guid.NewGuid():N}", descricao = "", senha = "senha-de-teste-123" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        TwoFactorTestClient.NextStep(fixture.Clock);

        var response = await client.PostAsJsonAsync($"/applications/{id}/secrets", new { codigo = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(CallerPassword.InvalidMessage, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhoHasNoPassword_ConfirmsWithTheAppCode()
    {
        var (admin, enabled) = await PasswordlessAdminAsync("sem-senha-convida");
        var invited = LocalUserSeed.UniqueEmail("convidado-sem-senha");
        TwoFactorTestClient.NextStep(fixture.Clock);

        var response = await ClientFor(admin).PostAsJsonAsync("/users/invitations",
            new { nome = "Pessoa", email = invited, papel = "leitor", codigo = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(await ExistsAsync(invited));
    }

    [Fact]
    public async Task TheAppCode_DoesNotWorkTwice()
    {
        var (admin, enabled) = await PasswordlessAdminAsync("codigo-duas-vezes");
        TwoFactorTestClient.NextStep(fixture.Clock);
        var code = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock);
        var client = ClientFor(admin);

        var first = await client.PostAsJsonAsync("/users/invitations", new { nome = "A", email = LocalUserSeed.UniqueEmail("dupla-a"), papel = "leitor", codigo = code });
        var second = await client.PostAsJsonAsync("/users/invitations", new { nome = "B", email = LocalUserSeed.UniqueEmail("dupla-b"), papel = "leitor", codigo = code });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Contains(CallerPassword.InvalidCodeMessage, await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await FailuresAsync(admin.NormalizedEmail));
    }

    [Fact]
    public async Task ARecoveryCode_DoesNotConfirm_AndStaysUnused()
    {
        var (admin, enabled) = await PasswordlessAdminAsync("recuperacao-nao");
        var invited = LocalUserSeed.UniqueEmail("convidado-recuperacao");

        var response = await ClientFor(admin).PostAsJsonAsync("/users/invitations",
            new { nome = "Pessoa", email = invited, papel = "leitor", codigo = enabled.RecoveryCodes[0] });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(CallerPassword.InvalidCodeMessage, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await ExistsAsync(invited));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, await db.UserRecoveryCodes.CountAsync(code => code.UserId == admin.Id && code.UsedAt == null));
    }

    [Fact]
    public async Task APasswordlessAdmin_PromotesWithTheAppCode()
    {
        var (admin, enabled) = await PasswordlessAdminAsync("sem-senha-promove");
        var target = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("promovido"), "senha-de-teste-123", role: UserRole.Operator);
        TwoFactorTestClient.NextStep(fixture.Clock);

        var response = await ClientFor(admin).PutAsJsonAsync($"/users/{target.Id}/role", new { papel = "admin", codigo = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"papel\":\"admin\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APasswordlessAdmin_CreatesAnApplicationWithTheAppCode()
    {
        var (admin, enabled) = await PasswordlessAdminAsync("sem-senha-aplicacao");
        TwoFactorTestClient.NextStep(fixture.Clock);

        var response = await ClientFor(admin).PostAsJsonAsync("/applications/",
            new { nome = $"Portal do Parceiro {Guid.NewGuid():N}", descricao = "", codigo = TwoFactorTestClient.CodeNow(enabled.Secret, fixture.Clock) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PasswordLogin_OfWhoHasNoPassword_IsTheGenericRefusal_AndCounts()
    {
        var (admin, _) = await PasswordlessAdminAsync("login-sem-senha");

        var response = await LocalUserSeed.PostSessionAsync(SignedInUser.Anonymous(fixture), admin.Email, "qualquer-senha-123");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Contains("E-mail ou senha inválidos.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, await FailuresAsync(admin.NormalizedEmail));
    }

    [Fact]
    public async Task TheResetLink_WritesTheFirstPassword()
    {
        var (person, _) = await PasswordlessAdminAsync("primeira-senha");
        var token = await InvitationSeed.PasswordResetAsync(fixture.Services, person.Id);
        var client = SignedInUser.Anonymous(fixture);
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/password-reset/complete")
        {
            Content = JsonContent.Create(new { token, senha = "primeira-senha-dela-123" }),
        };
        request.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);

        var completed = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        Assert.NotNull((await SignedInUser.ReloadAsync(fixture.Services, person.Id)).PasswordHash);
        await LocalUserSeed.AssertPasswordAcceptedAsync(LocalUserSeed.PostSessionAsync(client, person.Email, "primeira-senha-dela-123"), "totp");
        var users = await ClientFor(await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("le-lista"), "senha-de-teste-123"))
            .GetFromJsonAsync<System.Text.Json.JsonElement>($"/users/{person.Id}");
        Assert.Equal("senha", users.GetProperty("formasDeEntrada")[0].GetString());
    }

    // ---- Apoio ----

    /// <summary>Um Admin ativo sem senha, com a verificação ligada: como fica quem aceitou pelo Google (#274).</summary>
    private async Task<(User User, TwoFactorTestClient.Enabled Enabled)> PasswordlessAdminAsync(string prefix)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = User.Create(LocalUserSeed.UniqueEmail(prefix), "Pessoa Sem Senha", passwordHash: null, fixture.Clock.GetUtcNow(), UserRole.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, await TwoFactorTestClient.SeedEnabledAsync(fixture.Services, user.Id));
    }

    private HttpClient ClientFor(User user)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestIssuerTokens.ForClient(fixture.Services, "buteco-painel", user.Id.ToString(), "admin"));
        return client;
    }

    private async Task<int> FailuresAsync(string normalizedEmail)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LoginThrottles.Where(row => row.NormalizedEmail == normalizedEmail).Select(row => row.ConsecutiveFailures).SingleOrDefaultAsync();
    }

    private async Task<bool> ExistsAsync(string email)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = EmailNormalization.Normalize(email);
        return await db.Users.AnyAsync(user => user.NormalizedEmail == normalized);
    }
}
