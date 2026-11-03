using System.Text.Json;
using Buteco.Api.Identity.Clients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Pedido de token dos clientes de serviço pelo <c>/connect/token</c> real (design.md da change
/// clientes-servico-emissor, D1 e D2), com os segredos de teste de <see cref="TestAuthentication"/>.
/// Os clientes nascem no aquecimento, em segundo plano: <see cref="EnsureAsync"/> espera os dois.
/// </summary>
public static class ServiceClientTokens
{
    public static Task EnsureAsync(IServiceProvider services) => UserBootstrapFixture.WaitForAsync(async () =>
    {
        await using var scope = services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.FindByClientIdAsync(IdentityClients.InboxServiceClientId) is not null &&
            await applications.FindByClientIdAsync(IdentityClients.ConnectorsServiceClientId) is not null;
    });

    public static string SecretOf(string clientId) => clientId switch
    {
        IdentityClients.InboxServiceClientId => TestAuthentication.InboxServiceClientSecret,
        IdentityClients.ConnectorsServiceClientId => TestAuthentication.ConnectorsServiceClientSecret,
        _ => throw new ArgumentOutOfRangeException(nameof(clientId), clientId, "Não é cliente de serviço."),
    };

    public static HttpClient AnonymousClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public static Task<HttpResponseMessage> RequestAsync(
        WebApplicationFactory<Program> factory,
        string clientId,
        string? secret = null,
        string scope = IdentityClients.ApiScope) =>
        AnonymousClient(factory).PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = secret ?? SecretOf(clientId),
            [Parameters.Scope] = scope,
        }));

    /// <summary>O access token do cliente; falha o teste se o emissor não o emitir.</summary>
    public static async Task<string> AccessTokenAsync(WebApplicationFactory<Program> factory, string clientId)
    {
        var response = await RequestAsync(factory, clientId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
    }
}
