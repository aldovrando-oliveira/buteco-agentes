using System.Text.Json;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.ServiceTokens;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O token que o <c>apps/api</c> gera no processo para o <c>apps/connectors</c> (spec
/// <c>service-client-credentials</c>, "apps/api gera o próprio token de serviço no processo,
/// estreito"; design.md da change servicos-token-emissor, D1 e D2). O formato é comparado com
/// um token do <c>/connect/token</c> real, cabeçalho e nomes de claims, sem exceção.
/// </summary>
public class ServiceAccessTokenIssuerTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    private const string Issuer = ApiFactoryFixture.PublicBaseUrl + "/";

    public Task InitializeAsync() => ServiceClientTokens.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TheTokenGeneratedInProcess_HasTheSameShape_AsTheOneFromTheTokenEndpoint()
    {
        // O pedido ao /connect/token passa pelo gate e deixa o anel carregado.
        var endpointToken = new JsonWebToken(await ServiceClientTokens.AccessTokenAsync(factory, IdentityClients.InboxServiceClientId));
        var issuedToken = new JsonWebToken(await factory.Services.GetRequiredService<ServiceAccessTokenIssuer>().GetAsync(CancellationToken.None));

        var signingKeyId = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>()
            .CurrentValue.SigningCredentials.First().Kid;

        // Cabeçalho.
        Assert.Equal("at+jwt", issuedToken.Typ);
        Assert.Equal(endpointToken.Typ, issuedToken.Typ);
        Assert.Equal(endpointToken.Alg, issuedToken.Alg);
        Assert.Equal(endpointToken.Kid, issuedToken.Kid);
        Assert.Equal(signingKeyId, issuedToken.Kid);
        Assert.Equal(
            endpointToken.GetHeaderNames().Order(StringComparer.Ordinal),
            issuedToken.GetHeaderNames().Order(StringComparer.Ordinal));

        // Claims de contrato.
        Assert.Equal(Issuer, issuedToken.Issuer);
        Assert.Equal(endpointToken.Issuer, issuedToken.Issuer);
        Assert.Equal("buteco-servico-api", issuedToken.Subject);
        Assert.Equal("buteco-servico-api", issuedToken.GetPayloadValue<string>(Claims.ClientId));
        Assert.Equal([IdentityClients.ConnectorsScope], issuedToken.Audiences);
        Assert.Equal(IdentityClients.ConnectorsScope, issuedToken.GetPayloadValue<string>(Claims.Scope));
        Assert.Equal(TimeSpan.FromMinutes(15), issuedToken.ValidTo - issuedToken.IssuedAt);

        // A mesma duração que o OpenIddict dá a todo access token (leitura-identidade-emissor, D6).
        var accessTokenLifetime = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>()
            .CurrentValue.AccessTokenLifetime;
        Assert.Equal(accessTokenLifetime, issuedToken.ValidTo - issuedToken.IssuedAt);

        // O mesmo conjunto de nomes de claims, sem exceção (decisão do dono, Q3).
        Assert.True(ClaimNames(endpointToken).SequenceEqual(ClaimNames(issuedToken)),
            $"/connect/token: {Base64UrlEncoder.Decode(endpointToken.EncodedPayload)}\nno processo: {Base64UrlEncoder.Decode(issuedToken.EncodedPayload)}");
    }

    private static IEnumerable<string> ClaimNames(JsonWebToken token) =>
        JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload)).RootElement
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}

file static class JsonWebTokenHeaderNames
{
    public static IEnumerable<string> GetHeaderNames(this JsonWebToken token) =>
        JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedHeader)).RootElement
            .EnumerateObject().Select(property => property.Name).ToArray();
}
