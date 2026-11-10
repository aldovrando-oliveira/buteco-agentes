using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// As rotas de <c>/applications</c> (spec <c>external-applications</c>; change
/// aplicacoes-externas-credencial, D3 e D4; seções 2 e 5 do tasks.md). Os casos que erram a senha usam um
/// Admin próprio, para a contagem do bloqueio de um não alcançar os outros.
/// </summary>
public class ExternalApplicationEndpointsTests(ExternalApplicationsFixture factory) : IClassFixture<ExternalApplicationsFixture>
{
    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..(prefix.Length + 9)];

    // "Criação devolve o client ID e o segredo uma vez", e o cliente do OpenIddict do D3.
    [Fact]
    public async Task Create_ReturnsTheClientIdAndTheSecretOnce_AndRegistersANarrowClient()
    {
        var application = await factory.CreateAsync(Unique("Portal do Parceiro"), "Autoatendimento de revendas");

        Assert.Matches("^btc_app_[23456789abcdefghijkmnpqrstuvwxyz]{8}$", application.ClientId);
        Assert.Matches("^btc_sk_[A-Za-z0-9_-]{43}$", application.Secret);
        Assert.Equal($"{ApiFactoryFixture.PublicBaseUrl}/connect/token", application.Body.GetProperty("endpointToken").GetString());
        var detail = await (await factory.AdminClientAsync()).GetStringAsync($"/applications/{application.Id}");
        Assert.DoesNotContain(application.Secret, detail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"valor\"", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("\"hash\"", detail, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, (await applications.FindByClientIdAsync(application.ClientId))!);
        Assert.Equal(ClientTypes.Confidential, descriptor.ClientType);
        Assert.Equal(
            new[] { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials, Permissions.Prefixes.Scope + IdentityClients.ApiScope }.Order(),
            descriptor.Permissions.Order());
        Assert.Empty(descriptor.Settings);
    }

    // "Senha errada não cria".
    [Fact]
    public async Task WrongPassword_CreatesNothing()
    {
        var admin = await SignedInUser.CreateAsync(factory, "aplicacoes-senha-errada");
        var name = Unique("Senha errada");

        var response = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = name, senha = "senha-errada" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Senha inválida.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(0, await factory.WithDbAsync(db => db.ExternalApplications.CountAsync(application => application.Name == name)));
    }

    // "Corpo inválido não conta no bloqueio".
    [Fact]
    public async Task AnInvalidBody_IsRefusedBeforeThePassword_AndDoesNotCountTowardsTheLock()
    {
        var admin = await SignedInUser.CreateAsync(factory, "aplicacoes-corpo");

        var empty = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = " ", senha = "senha-errada" });
        var tooLong = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = new string('x', 81), descricao = new string('y', 281), senha = admin.Password });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("Informe o nome.", (await empty.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("nome")[0].GetString());
        var errors = (await tooLong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Equal("O nome tem no máximo 80 caracteres.", errors.GetProperty("nome")[0].GetString());
        Assert.Equal("A descrição tem no máximo 280 caracteres.", errors.GetProperty("descricao")[0].GetString());
        Assert.False(await factory.WithDbAsync(db => db.LoginThrottles.AnyAsync(row => row.NormalizedEmail == admin.User.NormalizedEmail)));
    }

    // "Nome repetido, sem diferenciar maiúscula", contra uma aplicação desativada.
    [Fact]
    public async Task ARepeatedName_IgnoringCase_IsConflict_EvenAgainstADeactivatedOne()
    {
        var name = Unique("CRM legado");
        var existing = await factory.CreateAsync(name);
        var admin = await factory.AdminAsync();
        await factory.As(admin).PostAsync($"/applications/{existing.Id}/deactivate", null);

        var response = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = name.ToUpperInvariant(), senha = admin.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Já existe uma aplicação com este nome.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    // "Bloqueado antes de tudo".
    [Fact]
    public async Task ALockedAdmin_Gets423_BeforeTheBodyIsRead()
    {
        var admin = await SignedInUser.CreateAsync(factory, "aplicacoes-bloqueado");
        var until = factory.Clock.GetUtcNow() + TimeSpan.FromMinutes(10);
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO identity_login_throttle (normalized_email, consecutive_failures, last_lock_minutes, locked_until, last_failure_at) VALUES ({admin.User.NormalizedEmail}, 5, 10, {until}, {until})"));

        var response = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = "" });

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    // "Client ID enviado no corpo é ignorado".
    [Fact]
    public async Task AClientIdInTheBody_IsIgnored()
    {
        var admin = await factory.AdminAsync();

        var response = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = Unique("Com client id"), clientId = "buteco-painel", senha = admin.Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("btc_app_", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString(), StringComparison.Ordinal);
    }

    // (guarda 2.2) "Prefixo reservado nunca é gravado". Defeito: as chamadas do Ensure removidas.
    [Fact]
    public async Task TheReservedPrefix_IsNeverWritten()
    {
        var admin = await factory.AdminAsync();
        var clientId = $"buteco-x{Guid.NewGuid():N}"[..16];
        factory.ClientIds.Forced = clientId;
        try
        {
            try
            {
                var response = await factory.As(admin).PostAsJsonAsync("/applications/", new { nome = Unique("Prefixo"), senha = admin.Password });
                Assert.False(response.IsSuccessStatusCode, $"criou com {clientId}");
            }
            catch (InvalidOperationException)
            {
                // O TestServer pode repassar a exceção em vez do 500.
            }
        }
        finally
        {
            factory.ClientIds.Forced = null;
        }

        Assert.False(await factory.WithDbAsync(db => db.ExternalApplications.AnyAsync(application => application.ClientId == clientId)));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().FindByClientIdAsync(clientId));
    }

    // "Busca por client ID", "Desativadas continuam listadas, depois das ativas", lista vazia.
    [Fact]
    public async Task TheList_SearchesByNameOrClientId_IgnoringCase_AndPutsTheActiveFirst()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var zeta = await factory.CreateAsync($"Zeta {tag}");
        var alfa = await factory.CreateAsync($"Alfa {tag}");
        var client = await factory.AdminClientAsync();
        await client.PostAsync($"/applications/{alfa.Id}/deactivate", null);

        var byName = await client.GetFromJsonAsync<JsonElement>($"/applications/?busca={tag.ToUpperInvariant()}");
        var byClientId = await client.GetFromJsonAsync<JsonElement>($"/applications/?busca={zeta.ClientId[^6..].ToUpperInvariant()}");
        var active = await client.GetFromJsonAsync<JsonElement>($"/applications/?busca={tag}&estado=ativas");
        var deactivated = await client.GetFromJsonAsync<JsonElement>($"/applications/?busca={tag}&estado=desativadas");
        var none = await client.GetFromJsonAsync<JsonElement>($"/applications/?busca=nada-{Guid.NewGuid():N}");

        Assert.Equal([zeta.Id, alfa.Id], byName.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.False(byName[1].GetProperty("ativa").GetBoolean());
        Assert.Equal(zeta.Id, Assert.Single(byClientId.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(zeta.Id, Assert.Single(active.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(alfa.Id, Assert.Single(deactivated.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(0, none.GetArrayLength());
    }

    // "Estado desconhecido".
    [Fact]
    public async Task AnUnknownState_Is400()
    {
        var response = await (await factory.AdminClientAsync()).GetAsync("/applications/?estado=todas");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("O estado precisa ser ativas ou desativadas.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("estado")[0].GetString());
    }

    // "Os quatro estados", e o 404 do detalhe.
    [Fact]
    public async Task TheDetail_DerivesTheFourStates_AndAnUnknownIdIs404()
    {
        var application = await factory.CreateAsync();
        var client = await factory.AdminClientAsync();
        var second = await GenerateAsync(application.Id);
        await client.PostAsync($"/applications/{application.Id}/secrets/{application.SecretId}/revoke", null);
        var now = factory.Clock.GetUtcNow();
        await factory.WithDbAsync(db => db.ExternalApplicationSecrets.Where(secret => secret.Id == second)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAt, now - TimeSpan.FromMinutes(1))));
        var third = await GenerateAsync(application.Id);
        var fourth = await GenerateAsync(application.Id);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/applications/{application.Id}");
        var states = detail.GetProperty("segredos").EnumerateArray().ToDictionary(secret => secret.GetProperty("id").GetGuid(), secret => secret.GetProperty("estado").GetString());
        var missing = await client.GetAsync($"/applications/{Guid.NewGuid()}");

        Assert.Equal("revogado", states[application.SecretId]);
        Assert.Equal("expirado", states[second]);
        Assert.Equal("substituido", states[third]);
        Assert.Equal("ativo", states[fourth]);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Aplicação não encontrada.", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    // "Renomear não muda a credencial", e os 400 e 409 da edição.
    [Fact]
    public async Task Rename_KeepsTheCredential_UpdatesTheDisplayName_AndValidates()
    {
        var application = await factory.CreateAsync();
        var other = await factory.CreateAsync();
        var client = await factory.AdminClientAsync();
        var newName = Unique("Portal de Revendas");

        var renamed = await client.PutAsJsonAsync($"/applications/{application.Id}", new { nome = newName, descricao = "Nova" });
        var empty = await client.PutAsJsonAsync($"/applications/{application.Id}", new { nome = "" });
        var repeated = await client.PutAsJsonAsync($"/applications/{application.Id}", new { nome = other.Body.GetProperty("nome").GetString() });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(application.ClientId, (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            Assert.Equal(newName, await applications.GetDisplayNameAsync((await applications.FindByClientIdAsync(application.ClientId))!));
        }

        await factory.AccessTokenAsync(application.ClientId, application.Secret);
    }

    // (guarda 5.6) "Aplicação desativada não obtém token" (external-applications, Q13). Três rodadas de
    // defeito: a desativação sem a revogação (reprova a asserção dos segredos revogados); a desativação sem a
    // revogação E o gerente sem o filtro de ativa (reprova o invalid_client); e o caso seguinte, da transação.
    [Fact]
    public async Task ADeactivatedApplication_GetsNoToken_AndAllItsSecretsAreRevoked()
    {
        var application = await factory.CreateAsync();
        await GenerateAsync(application.Id);

        var response = await (await factory.AdminClientAsync()).PostAsync($"/applications/{application.Id}/deactivate", null);
        var token = await factory.TokenAsync(application.ClientId, application.Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(token));
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(detail.GetProperty("ativa").GetBoolean());
        Assert.All(detail.GetProperty("segredos").EnumerateArray(), secret => Assert.Equal("revogado", secret.GetProperty("estado").GetString()));
    }

    // (guarda 5.6, rodada 3) A desativação e a revogação numa transação (Q13): o banco falhando na gravação
    // da desativação não deixa segredo revogado numa aplicação ativa. Defeito: a revogação fora da transação.
    [Fact]
    public async Task ADeactivationThatFailsHalfway_LeavesTheApplicationAndItsSecretsAsTheyWere()
    {
        var application = await factory.CreateAsync();
        factory.Failures.Armed = "UPDATE external_applications";
        try
        {
            try
            {
                var response = await (await factory.AdminClientAsync()).PostAsync($"/applications/{application.Id}/deactivate", null);
                Assert.False(response.IsSuccessStatusCode);
            }
            catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
            {
                // O TestServer pode repassar a falha simulada em vez do 500.
            }
        }
        finally
        {
            factory.Failures.Armed = null;
        }

        var (deactivatedAt, revokedAt) = await factory.WithDbAsync(async db => (
            await db.ExternalApplications.AsNoTracking().Where(candidate => candidate.Id == application.Id).Select(candidate => candidate.DeactivatedAt).SingleAsync(),
            await db.ExternalApplicationSecrets.AsNoTracking().Where(secret => secret.Id == application.SecretId).Select(secret => secret.RevokedAt).SingleAsync()));
        Assert.Null(deactivatedAt);
        Assert.Null(revokedAt);
    }

    // "Reativar não devolve o segredo antigo".
    [Fact]
    public async Task Reactivating_DoesNotBringTheOldSecretBack()
    {
        var application = await factory.CreateAsync();
        var client = await factory.AdminClientAsync();
        await client.PostAsync($"/applications/{application.Id}/deactivate", null);

        var reactivated = await client.PostAsync($"/applications/{application.Id}/activate", null);
        var token = await factory.TokenAsync(application.ClientId, application.Secret);

        var detail = await reactivated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detail.GetProperty("ativa").GetBoolean());
        Assert.DoesNotContain(detail.GetProperty("segredos").EnumerateArray(), secret => secret.GetProperty("estado").GetString() == "ativo");
        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(token));
    }

    // "Excluir não existe".
    [Fact]
    public async Task DeletingDoesNotExist()
    {
        var application = await factory.CreateAsync();
        var client = await factory.AdminClientAsync();

        var response = await client.DeleteAsync($"/applications/{application.Id}");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/applications/{application.Id}")).StatusCode);
    }

    // "O anterior vale mais 7 dias".
    [Fact]
    public async Task GeneratingASecret_LimitsThePreviousTo7Days_AndBothIssueTokens()
    {
        var application = await factory.CreateAsync();
        var admin = await factory.AdminAsync();
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        var response = await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = admin.Password });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = await factory.As(admin).GetFromJsonAsync<JsonElement>($"/applications/{application.Id}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var previous = detail.GetProperty("segredos").EnumerateArray().Single(secret => secret.GetProperty("id").GetGuid() == application.SecretId);
        Assert.Equal("substituido", previous.GetProperty("estado").GetString());
        Assert.Equal(created.GetProperty("criadoEm").GetDateTimeOffset() + TimeSpan.FromDays(7), previous.GetProperty("validoAte").GetDateTimeOffset());
        await factory.AccessTokenAsync(application.ClientId, application.Secret);
        await factory.AccessTokenAsync(application.ClientId, created.GetProperty("valor").GetString()!);
    }

    // (guarda 5.9) "O anterior que vence antes não ganha prazo". Defeito: o min trocado por atribuição direta.
    [Fact]
    public async Task GeneratingASecret_NeverExtendsAPreviousThatExpiresSooner()
    {
        var application = await factory.CreateAsync();
        var soon = factory.Clock.GetUtcNow() + TimeSpan.FromDays(2);
        await factory.WithDbAsync(db => db.ExternalApplicationSecrets.Where(secret => secret.Id == application.SecretId)
            .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.ExpiresAt, soon)));

        await GenerateAsync(application.Id);

        var expiresAt = await factory.WithDbAsync(db => db.ExternalApplicationSecrets.AsNoTracking()
            .Where(secret => secret.Id == application.SecretId).Select(secret => secret.ExpiresAt).SingleAsync());
        Assert.Equal(soon, expiresAt);
    }

    // (guarda 5.10) "Quarto segredo vivo recusado". Defeito: o limite removido.
    [Fact]
    public async Task TheFourthAliveSecret_IsConflict_AndNothingIsCreated()
    {
        var application = await factory.CreateAsync();
        await GenerateAsync(application.Id);
        await GenerateAsync(application.Id);
        var admin = await factory.AdminAsync();

        var response = await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = admin.Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Revogue um segredo antes de gerar outro.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(3, await factory.WithDbAsync(db => db.ExternalApplicationSecrets.CountAsync(secret => secret.ApplicationId == application.Id)));
    }

    // "Senha errada não gera", e o 409 da aplicação desativada.
    [Fact]
    public async Task GeneratingASecret_NeedsThePassword_AndAnActiveApplication()
    {
        var application = await factory.CreateAsync();
        var admin = await SignedInUser.CreateAsync(factory, "aplicacoes-gerar");

        var wrong = await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = "senha-errada" });
        await factory.As(admin).PostAsync($"/applications/{application.Id}/deactivate", null);
        var deactivated = await factory.As(admin).PostAsJsonAsync($"/applications/{application.Id}/secrets", new { senha = admin.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Senha inválida.", (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);
        Assert.Equal("Reative a aplicação antes de gerar um segredo.", (await deactivated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(1, await factory.WithDbAsync(db => db.ExternalApplicationSecrets.CountAsync(secret => secret.ApplicationId == application.Id)));
        Assert.True(await factory.WithDbAsync(db => db.LoginThrottles.AnyAsync(row => row.NormalizedEmail == admin.User.NormalizedEmail && row.ConsecutiveFailures == 1)));
    }

    // "Revogado recusado no próximo pedido", "Segredo de outra aplicação", e revogar o já revogado.
    [Fact]
    public async Task Revoking_RefusesTheNextTokenRequest_ScopesToTheApplication_AndIsIdempotent()
    {
        var application = await factory.CreateAsync();
        var other = await factory.CreateAsync();
        var client = await factory.AdminClientAsync();

        var revoked = await client.PostAsync($"/applications/{application.Id}/secrets/{application.SecretId}/revoke", null);
        var firstDate = await RevokedAtAsync(application.SecretId);
        var again = await client.PostAsync($"/applications/{application.Id}/secrets/{application.SecretId}/revoke", null);
        var crossed = await client.PostAsync($"/applications/{application.Id}/secrets/{other.SecretId}/revoke", null);
        var token = await factory.TokenAsync(application.ClientId, application.Secret);

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(firstDate, await RevokedAtAsync(application.SecretId));
        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
        Assert.Equal("Segredo não encontrado.", (await crossed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Null(await RevokedAtAsync(other.SecretId));
        Assert.Equal("invalid_client", await ExternalApplicationsFixture.TokenErrorAsync(token));
    }

    // "Operador e Leitor não veem aplicações" e "Admin rebaixado perde Aplicações na requisição seguinte".
    [Fact]
    public async Task OnlyTheRecordedAdmin_SeesApplications()
    {
        var operatorUser = await SignedInUser.CreateAsync(factory, "aplicacoes-operador", role: UserRole.Operator);
        var reader = await SignedInUser.CreateAsync(factory, "aplicacoes-leitor", role: UserRole.Reader);
        var demoted = await SignedInUser.CreateAsync(factory, "aplicacoes-rebaixado");
        await LocalUserSeed.SetRoleAsync(factory.Services, demoted.User.Id, UserRole.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.As(operatorUser).GetAsync("/applications/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.As(reader).GetAsync("/applications/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.As(demoted).GetAsync("/applications/")).StatusCode);
    }

    // O relógio do host é parado (FakeTimeProvider): sem avançar, dois segredos nasceriam no mesmo
    // instante, e "o mais novo" (o que torna o outro substituído) ficaria a cargo do GUID. Avançar é seguro
    // para os tokens: o OpenIddict não põe nbf, e o JwtBearer só confere o exp contra o relógio de parede.
    private async Task<Guid> GenerateAsync(Guid applicationId)
    {
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var admin = await factory.AdminAsync();
        var response = await factory.As(admin).PostAsJsonAsync($"/applications/{applicationId}/secrets", new { senha = admin.Password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body.ToString());
        return body.GetProperty("id").GetGuid();
    }

    private Task<DateTimeOffset?> RevokedAtAsync(Guid secretId) => factory.WithDbAsync(db =>
        db.ExternalApplicationSecrets.AsNoTracking().Where(secret => secret.Id == secretId).Select(secret => secret.RevokedAt).SingleAsync());
}
