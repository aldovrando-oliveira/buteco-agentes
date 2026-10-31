using System.Net;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A sincronização contra o OpenIddict real (spec <c>issuer-user-login</c>, "Cliente público
/// buteco-painel e três escopos mantidos pelo apps/api"; spec <c>oidc-issuer</c>, "Banco
/// recém-migrado tem só o cliente do painel"; design.md da change
/// usuarios-locais-login-emissor, D3.1).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a sincronização só criando, sem corrigir,
/// <see cref="ATamperedPanel_GoesBackToTheDescriptor"/> reprova (registro no tasks.md, 6.3).
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

    [Fact]
    public async Task TheWarmupCreates_OnlyThePanelClient_AndTheThreeScopes()
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

        Assert.Contains(IdentityClients.PanelClientId, clientIds);
        Assert.All(clientIds.Where(id => id!.StartsWith("buteco-", StringComparison.Ordinal)), id => Assert.Equal(IdentityClients.PanelClientId, id));

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
            line.EventId == IdentityClientCatalog.UndeclaredClientEvent && line.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            line.Message.Contains("buteco-outro", StringComparison.Ordinal));

        await using var check = factory.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().FindByClientIdAsync("buteco-outro");
        Assert.NotNull(stored);
        Assert.Equal("Cadastrado à mão", await check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().GetDisplayNameAsync(stored!));
    }
}
