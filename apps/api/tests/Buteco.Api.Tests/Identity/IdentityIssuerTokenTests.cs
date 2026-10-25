using System.Net;
using System.Text.Json;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Emissão de ponta a ponta (spec <c>oidc-issuer</c>; design.md da change
/// emissor-oidc-api, D2): o token de <c>client_credentials</c> é um JWT não cifrado,
/// verificável <b>só</b> com o JWKS baixado, com <c>iss</c>, <c>aud</c>, <c>sub</c>,
/// <c>iat</c>, <c>exp</c> e o <c>kid</c> da chave ativa do anel.
/// </summary>
public class IdentityIssuerTokenTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    private const string Issuer = ApiFactoryFixture.PublicBaseUrl + "/";

    public Task InitializeAsync() => IdentityIssuerTestClient.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClientCredentials_IssuesAJwt_VerifiableOnlyWithThePublishedJwks()
    {
        var client = AnonymousClient();

        var response = await client.PostAsync("/connect/token", IdentityIssuerTestClient.TokenRequest());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
        Assert.Equal(3, accessToken.Split('.').Length);

        var jwks = new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks"));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = IdentityIssuerTestClient.Resource,
            IssuerSigningKeys = jwks.GetSigningKeys(),
        });
        Assert.True(result.IsValid, result.Exception?.ToString());

        var token = (JsonWebToken)result.SecurityToken;
        var signingKid = factory.Services.GetRequiredService<IdentityKeyRing>().Snapshot!.SigningKid;
        Assert.Equal(signingKid, token.Kid);
        Assert.Equal(IdentityIssuerTestClient.ClientId, token.Subject);
        Assert.Contains(IdentityIssuerTestClient.Resource, token.Audiences);
        Assert.True(token.TryGetPayloadValue<long>("iat", out _));
        Assert.True(token.TryGetPayloadValue<long>("exp", out _));
    }

    // validacao-jwks-inbox-connectors, D6: o apps/inbox e o apps/connectors decidem o
    // subject pelo client_id e exigem typ at+jwt. Os dois vêm do próprio OpenIddict
    // (PrepareAccessTokenPrincipal, RFC 9068); o teste guarda esse contrato contra upgrade.
    [Fact]
    public async Task AccessToken_CarriesClientId_AndAtJwtType()
    {
        var response = await AnonymousClient().PostAsync("/connect/token", IdentityIssuerTestClient.TokenRequest());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        var accessToken = JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
        var token = new JsonWebToken(accessToken);

        Assert.True(token.TryGetPayloadValue<string>(Claims.ClientId, out var clientId), accessToken);
        Assert.Equal(IdentityIssuerTestClient.ClientId, clientId);
        Assert.Equal("at+jwt", token.Typ);
    }

    [Fact]
    public async Task WrongSecret_IsInvalidClient_AndIssuesNoToken()
    {
        var response = await AnonymousClient().PostAsync(
            "/connect/token",
            IdentityIssuerTestClient.TokenRequest(clientSecret: "segredo-errado"));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Errors.InvalidClient, document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty(Parameters.AccessToken, out _));
    }

    [Fact]
    public async Task UnknownClient_IsInvalidClient()
    {
        var response = await AnonymousClient().PostAsync(
            "/connect/token",
            IdentityIssuerTestClient.TokenRequest(clientId: "cliente-que-nao-existe", scope: null));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Errors.InvalidClient, document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PasswordGrant_IsUnsupportedGrantType()
    {
        var response = await AnonymousClient().PostAsync(
            "/connect/token",
            IdentityIssuerTestClient.TokenRequest(grantType: GrantTypes.Password, scope: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Errors.UnsupportedGrantType, document.RootElement.GetProperty("error").GetString());
    }

    private HttpClient AnonymousClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
