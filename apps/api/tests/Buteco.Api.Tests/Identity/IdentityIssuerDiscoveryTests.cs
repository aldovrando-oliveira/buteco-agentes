using System.Net;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Discovery e JWKS do emissor (spec <c>oidc-issuer</c>): anônimos, com o issuer da
/// configuração mesmo com outro <c>Host</c> na requisição, e o JWKS só com parâmetros
/// públicos.
/// </summary>
public class IdentityIssuerDiscoveryTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Issuer = ApiFactoryFixture.PublicBaseUrl + "/";

    [Fact]
    public async Task Discovery_IsAnonymous_AndDeclaresTheConfiguredIssuer_EvenWithAnotherHost()
    {
        // O TestServer atende em http, e o issuer é https: esquema e host diferentes.
        var client = AnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        request.Headers.Host = "outro-host.interno:8080";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Uri.UriSchemeHttp, request.RequestUri!.Scheme);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(Issuer, root.GetProperty("issuer").GetString());

        // Todo endereço de endpoint do documento, inclusive os que uma change futura
        // habilitar (e os aliases mTLS, que vêm num objeto aninhado), fica sob o issuer.
        var endpoints = EndpointAddresses(root).ToList();
        Assert.Contains(endpoints, e => e.Name == "token_endpoint");
        Assert.All(endpoints, e => Assert.True(
            e.Value.StartsWith(Issuer, StringComparison.Ordinal),
            $"'{e.Name}' = '{e.Value}' não está sob o issuer {Issuer}"));

        Assert.Equal(Issuer + ".well-known/jwks", root.GetProperty("jwks_uri").GetString());
        Assert.Equal(Issuer + "connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.DoesNotContain("outro-host", root.ToString());
    }

    private static IEnumerable<(string Name, string Value)> EndpointAddresses(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in EndpointAddresses(property.Value))
                {
                    yield return nested;
                }
            }
            else if (property.Name == "jwks_uri" || property.Name.EndsWith("_endpoint", StringComparison.Ordinal))
            {
                yield return (property.Name, property.Value.GetString() ?? "");
            }
        }
    }

    [Fact]
    public async Task Jwks_IsAnonymous_AndPublishesRsaSigningKeysWithKid_WithoutPrivateParameters()
    {
        var response = await AnonymousClient().GetAsync("/.well-known/jwks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = document.RootElement.GetProperty("keys").EnumerateArray().ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            Assert.Equal("RSA", key.GetProperty("kty").GetString());
            Assert.Equal("sig", key.GetProperty("use").GetString());
            Assert.False(string.IsNullOrEmpty(key.GetProperty("kid").GetString()));
            foreach (var privateParameter in new[] { "d", "p", "q", "dp", "dq", "qi" })
            {
                Assert.False(key.TryGetProperty(privateParameter, out _), $"o JWKS publicou o parâmetro privado '{privateParameter}'");
            }
        });
    }

    [Fact]
    public async Task TokenEndpoint_IsReachableWithoutToken_AndAnswersWithTheOAuthError()
    {
        var response = await AnonymousClient().PostAsync(
            "/connect/token",
            IdentityIssuerTestClient.TokenRequest(clientId: "cliente-que-nao-existe", scope: null));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_client", document.RootElement.GetProperty("error").GetString());
    }

    private HttpClient AnonymousClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
