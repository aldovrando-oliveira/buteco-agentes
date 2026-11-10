using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// O token da aplicação externa e o gerente estendido (spec <c>oidc-issuer</c>, "Token
/// client_credentials da aplicação externa" e "A aplicação externa é autenticada pelos segredos dela";
/// change aplicacoes-externas-credencial, D4, D5 e D6; seção 3 do tasks.md). Todo token sai do
/// <c>/connect/token</c> do host real, e a aplicação, da rota de criação.
/// </summary>
public class ExternalApplicationIssuerTests(ExternalApplicationsFixture factory) : IClassFixture<ExternalApplicationsFixture>
{
    // "Token da aplicação" (oidc-issuer).
    [Fact]
    public async Task TheApplicationToken_HasItsClientId_OnlyButecoApi_TheApplicationClaim_NoRole_And300Seconds()
    {
        var application = await factory.CreateAsync();

        var payload = ExternalApplicationsFixture.Payload(await factory.AccessTokenAsync(application.ClientId, application.Secret));

        Assert.Equal(application.ClientId, payload.GetProperty("client_id").GetString());
        Assert.Equal(application.ClientId, payload.GetProperty("sub").GetString());
        Assert.Equal("buteco-api", AudienceOf(payload).Single());
        Assert.Equal(application.Id.ToString(), payload.GetProperty("buteco_app").GetString());
        Assert.False(payload.TryGetProperty("role", out _));
        Assert.Equal(300, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
    }

    // (guarda 3.9) "exp − iat = 300 e 900" (oidc-issuer, "O painel continua com 15 minutos"; D5). Duas
    // rodadas de defeito: a duração removida do handler (a aplicação reprova com 900) e aplicada a todo
    // token, inclusive o do painel (o painel reprova com 300).
    [Fact]
    public async Task TheApplicationLives300Seconds_AndThePanel900()
    {
        var application = await factory.CreateAsync();
        var applicationToken = ExternalApplicationsFixture.Payload(await factory.AccessTokenAsync(application.ClientId, application.Secret));
        var panelToken = ExternalApplicationsFixture.Payload((await SignedInUser.CreateAsync(factory, "aplicacoes-painel")).AccessToken);

        Assert.Equal(300, applicationToken.GetProperty("exp").GetInt64() - applicationToken.GetProperty("iat").GetInt64());
        Assert.Equal(900, panelToken.GetProperty("exp").GetInt64() - panelToken.GetProperty("iat").GetInt64());
    }

    // (guarda 3.8) "Os clientes do Buteco nunca levam buteco_app" (oidc-issuer; Q1). Defeito: o claim posto
    // em todo token, sem consultar o cadastro.
    [Fact]
    public async Task TheButecoClients_NeverCarryTheApplicationClaim()
    {
        await factory.CreateAsync();
        await ServiceClientTokens.EnsureAsync(factory.Services);
        var tokens = new Dictionary<string, string>
        {
            [IdentityClients.PanelClientId] = (await SignedInUser.CreateAsync(factory, "aplicacoes-sem-claim")).AccessToken,
            [IdentityClients.InboxServiceClientId] = await ServiceClientTokens.AccessTokenAsync(factory, IdentityClients.InboxServiceClientId),
            [IdentityClients.ConnectorsServiceClientId] = await ServiceClientTokens.AccessTokenAsync(factory, IdentityClients.ConnectorsServiceClientId),
        };

        foreach (var (client, token) in tokens)
        {
            Assert.False(ExternalApplicationsFixture.Payload(token).TryGetProperty("buteco_app", out _), $"{client} levou buteco_app");
        }
    }

    // (guarda 3.2) "Segredo de enchimento nunca vale" (oidc-issuer). O enchimento é aleatório e descartado
    // na criação; para o caso conhecer um, o ClientSecret do cliente no OpenIddict é regravado com um texto
    // conhecido, pelo próprio OpenIddict (que grava o PBKDF2 dele). Defeito: o gerente caindo no base quando
    // nenhum segredo do cadastro confere — o base aceitaria esse texto.
    [Fact]
    public async Task TheFillerSecret_IsNeverAccepted()
    {
        var application = await factory.CreateAsync();
        const string known = "enchimento-conhecido-pelo-teste";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var client = await applications.FindByClientIdAsync(application.ClientId);
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(descriptor, client!);
            descriptor.ClientSecret = known;
            await applications.UpdateAsync(client!, descriptor);
        }

        var response = await factory.TokenAsync(application.ClientId, known);

        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(response));
    }

    // (guarda 3.3) "Cliente de serviço não passa pelo cadastro" (oidc-issuer). Um segredo de aplicação com o
    // mesmo texto do segredo do buteco-servico-inbox: o token do serviço sai pela conferência do OpenIddict, e
    // o segredo de aplicação continua nunca usado. Defeito: o gerente procurando o hash no cadastro para todo
    // cliente.
    [Fact]
    public async Task AServiceClient_DoesNotGoThroughTheRegistry()
    {
        var application = await factory.CreateAsync();
        await ServiceClientTokens.EnsureAsync(factory.Services);
        var inboxSecret = ServiceClientTokens.SecretOf(IdentityClients.InboxServiceClientId);
        var twin = ExternalApplicationSecret.Create(application.Id, inboxSecret, factory.Clock.GetUtcNow());
        await factory.WithDbAsync(async db =>
        {
            db.ExternalApplicationSecrets.Add(twin);
            await db.SaveChangesAsync();
        });

        await ServiceClientTokens.AccessTokenAsync(factory, IdentityClients.InboxServiceClientId);

        var lastUsed = await factory.WithDbAsync(db =>
            db.ExternalApplicationSecrets.AsNoTracking().Where(secret => secret.Id == twin.Id).Select(secret => secret.LastUsedAt).SingleAsync());
        Assert.Null(lastUsed);
    }

    // (guarda 3.4) "Segredo revogado ou vencido" (oidc-issuer). Duas rodadas de defeito: o gerente sem o
    // filtro de revogado; o gerente sem o filtro de vencido.
    [Fact]
    public async Task ARevokedSecret_AndAnExpiredSecret_AreInvalidClient()
    {
        var revoked = await factory.CreateAsync();
        var expired = await factory.CreateAsync();
        var now = factory.Clock.GetUtcNow();
        await factory.WithDbAsync(async db =>
        {
            await db.ExternalApplicationSecrets.Where(secret => secret.Id == revoked.SecretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.RevokedAt, now));
            await db.ExternalApplicationSecrets.Where(secret => secret.Id == expired.SecretId)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAt, now - TimeSpan.FromMinutes(1)));
        });

        var revokedResponse = await factory.TokenAsync(revoked.ClientId, revoked.Secret);
        var expiredResponse = await factory.TokenAsync(expired.ClientId, expired.Secret);

        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(revokedResponse));
        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(expiredResponse));
    }

    // "Substituído vale dentro da sobreposição" (oidc-issuer). O relógio do host não volta (é compartilhado
    // pela classe), então "8 dias depois" é o fim da sobreposição trazido para trás de agora, no banco.
    [Fact]
    public async Task ASupersededSecret_IsAcceptedWithinTheOverlap_AndRefusedAfterIt()
    {
        var application = await factory.CreateAsync();
        var admin = await factory.AdminAsync();
        var generated = await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = admin.Password });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);

        var within = await factory.TokenAsync(application.ClientId, application.Secret);
        var limit = factory.Clock.GetUtcNow();
        await factory.WithDbAsync(db => db.ExternalApplicationSecrets.Where(secret => secret.Id == application.SecretId)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAt, limit - TimeSpan.FromDays(1))));
        var after = await factory.TokenAsync(application.ClientId, application.Secret);

        Assert.Equal(HttpStatusCode.OK, within.StatusCode);
        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(after));
    }

    // "Segredo de outra aplicação" (oidc-issuer).
    [Fact]
    public async Task AnotherApplicationsSecret_IsInvalidClient()
    {
        var first = await factory.CreateAsync();
        var second = await factory.CreateAsync();

        var response = await factory.TokenAsync(first.ClientId, second.Secret);

        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(response));
    }

    // "Token emitido marca o uso" (external-applications, D6).
    [Fact]
    public async Task AnIssuedToken_MarksTheSecretAndTheApplicationAsUsed_AndNotTheOtherSecrets()
    {
        var application = await factory.CreateAsync();
        var admin = await factory.AdminAsync();
        await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = admin.Password });

        await factory.AccessTokenAsync(application.ClientId, application.Secret);

        var detail = await factory.As(admin).GetFromJsonAsync<JsonElement>($"/applications/{application.Id}");
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("ultimoUso").ValueKind);
        var secrets = detail.GetProperty("segredos").EnumerateArray().ToList();
        Assert.NotEqual(JsonValueKind.Null, secrets.Single(secret => secret.GetProperty("id").GetGuid() == application.SecretId).GetProperty("ultimoUso").ValueKind);
        Assert.Equal(JsonValueKind.Null, secrets.Single(secret => secret.GetProperty("id").GetGuid() != application.SecretId).GetProperty("ultimoUso").ValueKind);
    }

    // "Rajada grava uma vez" (external-applications, D6). O relógio do host é da classe e não avança aqui
    // (adiantá-lo carimbaria tokens no futuro); a rajada é o último uso já gravado há 30 s, como o de um
    // pedido anterior no mesmo minuto: cinco pedidos agora não o regravam. E gravado há 2 min, o pedido
    // seguinte regrava.
    [Fact]
    public async Task ABurstOfTokens_WritesTheLastUseAtMostOncePerMinute()
    {
        var application = await factory.CreateAsync();
        var now = factory.Clock.GetUtcNow();
        await SetLastUseAsync(application, now - TimeSpan.FromSeconds(30));

        for (var i = 0; i < 5; i++)
        {
            await factory.AccessTokenAsync(application.ClientId, application.Secret);
        }

        Assert.Equal((now - TimeSpan.FromSeconds(30), now - TimeSpan.FromSeconds(30)), await LastUseAsync(application));

        await SetLastUseAsync(application, now - TimeSpan.FromMinutes(2));
        await factory.AccessTokenAsync(application.ClientId, application.Secret);

        Assert.Equal((now, now), await LastUseAsync(application));
    }

    private Task SetLastUseAsync(ExternalApplicationsFixture.Created application, DateTimeOffset when) => factory.WithDbAsync(async db =>
    {
        await db.ExternalApplicationSecrets.Where(secret => secret.Id == application.SecretId)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.LastUsedAt, when));
        await db.ExternalApplications.Where(candidate => candidate.Id == application.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.LastUsedAt, when));
    });

    private Task<(DateTimeOffset?, DateTimeOffset?)> LastUseAsync(ExternalApplicationsFixture.Created application) => factory.WithDbAsync(async db => (
        await db.ExternalApplicationSecrets.AsNoTracking().Where(secret => secret.Id == application.SecretId).Select(secret => secret.LastUsedAt).SingleAsync(),
        await db.ExternalApplications.AsNoTracking().Where(candidate => candidate.Id == application.Id).Select(candidate => candidate.LastUsedAt).SingleAsync()));

    // "Escopo de outro app recusado para a aplicação" (oidc-issuer): invalid_request, como o do cliente
    // de serviço.
    [Fact]
    public async Task TheInboxScope_IsRefusedWithInvalidRequest()
    {
        var application = await factory.CreateAsync();

        var response = await factory.TokenAsync(application.ClientId, application.Secret, "buteco-inbox");

        Assert.Equal("invalid_request", await ExternalApplicationsFixture.TokenErrorAsync(response));
    }

    // "Sem refresh token" (oidc-issuer; herança da #209, D2).
    [Fact]
    public async Task OfflineAccess_GivesNoRefreshToken_AndWritesNoTokenNorAuthorization()
    {
        var application = await factory.CreateAsync();

        var response = await factory.TokenAsync(application.ClientId, application.Secret, "buteco-api offline_access");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("refresh_token", body, StringComparison.Ordinal);
        var rows = await factory.WithDbAsync(async db =>
        {
            var applicationKey = await db.Database.SqlQuery<string>(
                $"SELECT \"Id\" AS \"Value\" FROM openiddict_applications WHERE \"ClientId\" = {application.ClientId}").SingleAsync();
            return await db.Database.SqlQuery<int>(
                $"SELECT ((SELECT count(*) FROM openiddict_tokens WHERE \"ApplicationId\" = {applicationKey}) + (SELECT count(*) FROM openiddict_authorizations WHERE \"ApplicationId\" = {applicationKey}))::int AS \"Value\"").SingleAsync();
        });
        Assert.Equal(0, rows);
    }

    // "Sem scope, o apps/api recusa" (oidc-issuer; Q4): o token sai sem aud, e o apps/api responde 401.
    [Fact]
    public async Task WithoutScope_TheTokenHasNoAudience_AndTheApiAnswers401()
    {
        var application = await factory.CreateAsync();

        var token = await factory.AccessTokenAsync(application.ClientId, application.Secret, scope: null);
        var response = await factory.WithToken(token).GetAsync("/agents/");

        Assert.Empty(AudienceOf(ExternalApplicationsFixture.Payload(token)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static IReadOnlyList<string> AudienceOf(JsonElement payload) =>
        !payload.TryGetProperty("aud", out var aud) ? []
        : aud.ValueKind == JsonValueKind.Array ? aud.EnumerateArray().Select(value => value.GetString()!).ToList()
        : [aud.GetString()!];
}
