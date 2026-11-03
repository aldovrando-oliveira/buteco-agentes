using System.Text.Json;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O token <c>client_credentials</c> dos clientes de serviço (spec <c>oidc-issuer</c>,
/// "Clientes de serviço mantidos pelo apps/api"; design.md da change clientes-servico-emissor,
/// D1): <c>aud</c> só <c>buteco-api</c>, <c>client_id</c> do cliente, <c>typ</c>
/// <c>at+jwt</c>, verificável pelo JWKS. Escopo de outro app e segredo errado não emitem nada.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com <c>scp:buteco-connectors</c> no descritor
/// de serviço, <see cref="AnotherAppScope_IsRefused_WithoutToken"/> reprova (registro no
/// tasks.md, 3.3).
/// </para>
/// </summary>
public class ServiceClientTokenTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    private const string Issuer = ApiFactoryFixture.PublicBaseUrl + "/";

    public Task InitializeAsync() => ServiceClientTokens.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(IdentityClients.InboxServiceClientId)]
    [InlineData(IdentityClients.ConnectorsServiceClientId)]
    public async Task ClientCredentials_IssuesAnApiOnlyToken_ForTheServiceClient(string clientId)
    {
        var accessToken = await ServiceClientTokens.AccessTokenAsync(factory, clientId);

        var jwks = new JsonWebKeySet(await ServiceClientTokens.AnonymousClient(factory).GetStringAsync("/.well-known/jwks"));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = IdentityClients.ApiScope,
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        Assert.True(result.IsValid, result.Exception?.ToString());

        var token = (JsonWebToken)result.SecurityToken;
        Assert.Equal("at+jwt", token.Typ);
        Assert.Equal([IdentityClients.ApiScope], token.Audiences);
        Assert.Equal(clientId, token.Subject);
        Assert.True(token.TryGetPayloadValue<string>(Claims.ClientId, out var claimedClientId), accessToken);
        Assert.Equal(clientId, claimedClientId);
    }

    // O escopo existe, mas o cliente não tem permissão dele: o OpenIddict 7.7.2 recusa com
    // invalid_request e a descrição ID2051 ("This client application is not allowed to use the
    // specified scope."), em ValidateScopePermissions, OpenIddict.Server/
    // OpenIddictServerHandlers.Exchange.cs l. 1392-1394 (tag 7.7.2). O invalid_scope fica para
    // escopo que não existe. O texto da descrição é do OpenIddict, e não é afirmado aqui.
    [Fact]
    public async Task AnotherAppScope_IsRefused_WithoutToken()
    {
        var response = await ServiceClientTokens.RequestAsync(factory, IdentityClients.InboxServiceClientId, scope: IdentityClients.ConnectorsScope);
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(response.IsSuccessStatusCode, body);
        Assert.Equal(Errors.InvalidRequest, JsonDocument.Parse(body).RootElement.GetProperty(Parameters.Error).GetString());
        Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty(Parameters.AccessToken, out _), body);
    }

    [Fact]
    public async Task WrongSecret_IsInvalidClient()
    {
        var response = await ServiceClientTokens.RequestAsync(
            factory,
            IdentityClients.ConnectorsServiceClientId,
            secret: TestAuthentication.InboxServiceClientSecret);
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(response.IsSuccessStatusCode, body);
        Assert.Contains(Errors.InvalidClient, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Parameters.AccessToken, body, StringComparison.Ordinal);
    }
}
