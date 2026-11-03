using System.Net;
using Buteco.Api.Identity;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A sincronização contra o OpenIddict real (spec <c>issuer-user-login</c>, "Cliente público
/// buteco-painel e três escopos mantidos pelo apps/api"; spec <c>oidc-issuer</c>, "Banco
/// recém-migrado tem só os clientes do apps/api" e "Clientes de serviço mantidos pelo
/// apps/api"; design.md da change usuarios-locais-login-emissor, D3.1, e da change
/// clientes-servico-emissor, D2).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a sincronização só criando, sem corrigir,
/// <see cref="ATamperedPanel_GoesBackToTheDescriptor"/> reprova (registro no tasks.md, 6.3).
/// </para>
/// <para>
/// Os casos dos clientes de serviço, verificados contra três defeitos (registro no tasks.md
/// da change clientes-servico-emissor, 3.2): a correção com o descritor sem segredo faz o
/// OpenIddict recusar a atualização (<c>ID2113</c>), e todos os casos da classe reprovam, a
/// partir de <see cref="ATamperedServiceClient_GoesBackToTheDescriptor_AndKeepsItsCredential"/>;
/// a sincronização sem conferir o segredo faz
/// <see cref="AServiceClientSecretThatNoLongerValidates_IsRewritten"/> reprovar; e o log com o
/// segredo faz o mesmo caso reprovar.
/// </para>
/// </summary>
public class IdentityClientCatalogSyncTests(UserBootstrapFixture factory) : IClassFixture<UserBootstrapFixture>
{
    private async Task SyncAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
    }

    private async Task<OpenIddictApplicationDescriptor> PanelAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, (await applications.FindByClientIdAsync(IdentityClients.PanelClientId))!);
        return descriptor;
    }

    private async Task<OpenIddictApplicationDescriptor> ClientAsync(string clientId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, (await applications.FindByClientIdAsync(clientId))!);
        return descriptor;
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string clientId, string secret)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        return await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = secret,
            [Parameters.Scope] = IdentityClients.ApiScope,
        }));
    }

    [Fact]
    public async Task TheWarmupCreates_ThePanelAndTheServiceClients_AndTheThreeScopes()
    {
        await UserBootstrapFixture.WaitForAsync(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().FindByClientIdAsync(IdentityClients.PanelClientId) is not null;
        });

        await using var check = factory.Services.CreateAsyncScope();
        var applications = check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var clientIds = new List<string?>();
        await foreach (var application in applications.ListAsync(count: null, offset: null))
        {
            clientIds.Add(await applications.GetClientIdAsync(application));
        }

        Assert.Equal(
            new HashSet<string?> { IdentityClients.PanelClientId, IdentityClients.InboxServiceClientId, IdentityClients.ConnectorsServiceClientId },
            clientIds.Where(id => id!.StartsWith("buteco-", StringComparison.Ordinal) && id != "buteco-outro").ToHashSet());

        var scopes = check.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in IdentityClients.Scopes)
        {
            var scope = await scopes.FindByNameAsync(name);
            Assert.NotNull(scope);
            Assert.Equal([name], (await scopes.GetResourcesAsync(scope!)).ToArray());
        }

        var panel = await PanelAsync();
        Assert.Equal(ClientTypes.Public, panel.ClientType);
        Assert.Null(panel.ClientSecret);

        foreach (var clientId in new[] { IdentityClients.InboxServiceClientId, IdentityClients.ConnectorsServiceClientId })
        {
            var service = await ClientAsync(clientId);
            Assert.Equal(ClientTypes.Confidential, service.ClientType);
            Assert.False(string.IsNullOrEmpty(service.ClientSecret));
            Assert.Equal(
                new HashSet<string> { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials, Permissions.Prefixes.Scope + IdentityClients.ApiScope },
                service.Permissions);
        }
    }

    [Fact]
    public async Task ATamperedServiceClient_GoesBackToTheDescriptor_AndKeepsItsCredential()
    {
        await SyncAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = (await applications.FindByClientIdAsync(IdentityClients.InboxServiceClientId))!;
            var tampered = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(tampered, application);
            tampered.Permissions.Add(Permissions.Prefixes.Scope + IdentityClients.ConnectorsScope);
            tampered.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            await applications.UpdateAsync(application, tampered);
        }

        await SyncAsync();

        var service = await ClientAsync(IdentityClients.InboxServiceClientId);
        Assert.DoesNotContain(Permissions.Prefixes.Scope + IdentityClients.ConnectorsScope, service.Permissions);
        Assert.DoesNotContain(Permissions.GrantTypes.RefreshToken, service.Permissions);
        var response = await RequestTokenAsync(IdentityClients.InboxServiceClientId, TestAuthentication.InboxServiceClientSecret);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AServiceClientSecretThatNoLongerValidates_IsRewritten()
    {
        // O gravado diverge do configurado como depois de trocar o segredo no .env e subir o
        // apps/api: o hash no banco é de outro valor.
        const string previous = "c2VncmVkby1hbnRlcmlvci1kby1jb25uZWN0b3JzLTMyYnl0ZXM=";
        await SyncAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = (await applications.FindByClientIdAsync(IdentityClients.ConnectorsServiceClientId))!;
            await applications.UpdateAsync(application, previous);
        }

        Assert.NotEqual(HttpStatusCode.OK, (await RequestTokenAsync(IdentityClients.ConnectorsServiceClientId, TestAuthentication.ConnectorsServiceClientSecret)).StatusCode);

        await SyncAsync();

        var refused = await RequestTokenAsync(IdentityClients.ConnectorsServiceClientId, previous);
        Assert.Contains(Errors.InvalidClient, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await RequestTokenAsync(IdentityClients.ConnectorsServiceClientId, TestAuthentication.ConnectorsServiceClientSecret)).StatusCode);

        var synced = Assert.Single(factory.Logs.Lines, line =>
            line.EventId == IdentityClientCatalog.SyncedEvent &&
            line.Message.Contains($"segredo do cliente {IdentityClients.ConnectorsServiceClientId} trocado", StringComparison.Ordinal));
        Assert.DoesNotContain(previous, synced.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestAuthentication.ConnectorsServiceClientSecret, synced.Message, StringComparison.Ordinal);
    }

    // O isolamento por item (design.md da change clientes-servico-emissor, D2; convenção 4).
    // O descritor do inbox sai sem segredo: confidencial sem segredo, o OpenIddict recusa a
    // correção (ValidationException, ID2113). O connectors, adulterado também, é corrigido
    // depois do inbox, e o SaveChanges dele é onde a entidade recusada vazaria. Verificado
    // contra três defeitos (convenção 15; registro no tasks.md, 3.2): sem o try/catch por
    // item, o painel não é corrigido no mesmo ciclo; sem a falha no fim do ciclo, o
    // ThrowsAsync reprova; sem limpar o change tracker, o inbox recusado é gravado sem
    // segredo junto com o connectors, e o pedido de token do inbox reprova.
    [Fact]
    public async Task AServiceClientThatFailsToSync_DoesNotBlockThePanelCorrection_InTheSameCycle()
    {
        await SyncAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            var panel = (await applications.FindByClientIdAsync(IdentityClients.PanelClientId))!;
            var tamperedPanel = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(tamperedPanel, panel);
            tamperedPanel.Permissions.Add(Permissions.GrantTypes.Password);
            await applications.UpdateAsync(panel, tamperedPanel);

            foreach (var clientId in new[] { IdentityClients.InboxServiceClientId, IdentityClients.ConnectorsServiceClientId })
            {
                var service = (await applications.FindByClientIdAsync(clientId))!;
                var tampered = new OpenIddictApplicationDescriptor();
                await applications.PopulateAsync(tampered, service);
                tampered.Permissions.Add(Permissions.GrantTypes.RefreshToken);
                await applications.UpdateAsync(service, tampered);
            }
        }

        try
        {
            var logsBefore = factory.Logs.Lines.Count;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var configured = scope.ServiceProvider.GetRequiredService<IOptions<IdentityIssuerOptions>>().Value;
                var broken = new IdentityIssuerOptions
                {
                    SigningKeyEncryptionKey = configured.SigningKeyEncryptionKey,
                    TwoFactorEncryptionKey = configured.TwoFactorEncryptionKey,
                    PanelBaseUrl = configured.PanelBaseUrl,
                    ServiceClients = new ServiceClientsOptions
                    {
                        Inbox = new ServiceClientOptions { Secret = "" },
                        Connectors = configured.ServiceClients.Connectors,
                    },
                };
                var catalog = new IdentityClientCatalog(
                    scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(),
                    scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>(),
                    scope.ServiceProvider.GetRequiredService<IdentityIssuerState>(),
                    Microsoft.Extensions.Options.Options.Create(broken),
                    scope.ServiceProvider.GetRequiredService<ILogger<IdentityClientCatalog>>());

                // O ciclo grava os itens que deram certo e lança no fim, para o aquecimento repetir.
                var incomplete = await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.SyncAsync(CancellationToken.None));
                Assert.Contains($"cliente {IdentityClients.InboxServiceClientId}", incomplete.Message, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(Permissions.GrantTypes.Password, (await PanelAsync()).Permissions);
            Assert.Contains(Permissions.GrantTypes.RefreshToken, (await ClientAsync(IdentityClients.InboxServiceClientId)).Permissions);
            var failed = Assert.Single(factory.Logs.Lines.Skip(logsBefore), line =>
                line.EventId == IdentityClientCatalog.ItemFailedEvent && line.Level == LogLevel.Error &&
                line.Message.Contains($"cliente {IdentityClients.InboxServiceClientId}", StringComparison.Ordinal));
            Assert.DoesNotContain(TestAuthentication.InboxServiceClientSecret, failed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TestAuthentication.ConnectorsServiceClientSecret, failed.Message, StringComparison.Ordinal);

            // O connectors foi corrigido no mesmo ciclo, e o inbox ficou como estava: adulterado,
            // mas com a credencial dele.
            Assert.DoesNotContain(Permissions.GrantTypes.RefreshToken, (await ClientAsync(IdentityClients.ConnectorsServiceClientId)).Permissions);
            Assert.Equal(HttpStatusCode.OK, (await RequestTokenAsync(IdentityClients.InboxServiceClientId, TestAuthentication.InboxServiceClientSecret)).StatusCode);

            // O ciclo seguinte, com a configuração de verdade, corrige o inbox também.
            await SyncAsync();
            Assert.DoesNotContain(Permissions.GrantTypes.RefreshToken, (await ClientAsync(IdentityClients.InboxServiceClientId)).Permissions);
            Assert.Equal(HttpStatusCode.OK, (await RequestTokenAsync(IdentityClients.InboxServiceClientId, TestAuthentication.InboxServiceClientSecret)).StatusCode);
        }
        finally
        {
            // Um caso que reprove no meio não deixa clientes adulterados para os outros desta classe.
            await SyncAsync();
        }
    }

    [Fact]
    public async Task ATamperedPanel_GoesBackToTheDescriptor()
    {
        await SyncAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = (await applications.FindByClientIdAsync(IdentityClients.PanelClientId))!;
            var tampered = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(tampered, application);
            tampered.ClientType = ClientTypes.Confidential;
            tampered.ClientSecret = "segredo-adulterado";
            tampered.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
            await applications.UpdateAsync(application, tampered);
        }

        await SyncAsync();

        var panel = await PanelAsync();
        Assert.Equal(ClientTypes.Public, panel.ClientType);
        Assert.Null(panel.ClientSecret);
        Assert.DoesNotContain(Permissions.GrantTypes.ClientCredentials, panel.Permissions);
    }

    [Fact]
    public async Task ClientCredentials_WithThePanel_IssuesNoToken()
    {
        await SyncAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = IdentityClients.PanelClientId,
            [Parameters.Scope] = IdentityClients.ApiScope,
        }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Parameters.AccessToken, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUndeclaredButecoClient_IsOnlyLogged()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync("buteco-outro") is null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "buteco-outro",
                    ClientType = ClientTypes.Public,
                    DisplayName = "Cadastrado à mão",
                    Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.RefreshToken },
                });
            }
        }

        await SyncAsync();

        Assert.Contains(factory.Logs.Lines, line =>
            line.EventId == IdentityClientCatalog.UndeclaredClientEvent && line.Level == LogLevel.Error &&
            line.Message.Contains("buteco-outro", StringComparison.Ordinal));

        await using var check = factory.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().FindByClientIdAsync("buteco-outro");
        Assert.NotNull(stored);
        Assert.Equal("Cadastrado à mão", await check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().GetDisplayNameAsync(stored!));
    }
}
