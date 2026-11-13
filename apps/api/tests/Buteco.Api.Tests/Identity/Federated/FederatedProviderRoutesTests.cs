using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// As rotas dos provedores (spec <c>federated-providers</c>, "Rota só leitura dos provedores para a tela
/// Identidade" e "Rota anônima dos provedores para o login"; design.md da change provedores-federados-google,
/// D9), num host com <c>HostedDomain</c>, para afirmar que ele também não sai. Guardas 8.4 e 8.6.
/// </summary>
public class FederatedProviderRoutesTests(HostedDomainFederatedLoginFixture fixture) : IClassFixture<HostedDomainFederatedLoginFixture>
{
    [Fact]
    public async Task TheAdmin_ReadsTheProviders_WithoutTheSecret()
    {
        var admin = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("le-provedores"), "senha-de-teste-123");
        var linked = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("vinculado-provedores"), "senha-de-teste-123");
        await GoogleFlow.LinkAsync(fixture.Services, linked.Id, GoogleFlow.NewAccount(linked.Email));

        var response = await ClientFor(admin.Id, "admin").GetAsync("/identity/federated-providers");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var provider = Assert.Single(JsonDocument.Parse(raw).RootElement.GetProperty("provedores").EnumerateArray());
        Assert.Equal("corporativo", provider.GetProperty("chave").GetString());
        Assert.Equal("google", provider.GetProperty("tipo").GetString());
        Assert.Equal("Google", provider.GetProperty("nome").GetString());
        Assert.Equal("https://accounts.google.com/", provider.GetProperty("issuer").GetString());
        Assert.Equal(["login"], provider.GetProperty("usadoPara").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(FakeGoogle.ClientId, provider.GetProperty("clientId").GetString());
        Assert.EndsWith("/auth/federated/callback", provider.GetProperty("uriDeRetorno").GetString(), StringComparison.Ordinal);
        Assert.True(provider.GetProperty("vinculos").GetInt32() >= 1);
        Assert.DoesNotContain(FakeGoogle.ClientSecret, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("empresa.com", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(UserRole.Operator, "operator")]
    [InlineData(UserRole.Reader, "reader")]
    public async Task OperatorAndReader_AreRefused(UserRole role, string claim)
    {
        var person = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail($"provedores-{claim}"), "senha-de-teste-123", role: role);

        var response = await ClientFor(person.Id, claim).GetAsync("/identity/federated-providers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheLogin_ReadsOnlyKeyAndType()
    {
        var response = await GoogleFlow.Client(fixture).GetAsync("/auth/federated/providers");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var provider = Assert.Single(JsonDocument.Parse(raw).RootElement.GetProperty("provedores").EnumerateArray());
        Assert.Equal(["chave", "tipo"], provider.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("corporativo", provider.GetProperty("chave").GetString());
        Assert.Equal("google", provider.GetProperty("tipo").GetString());
    }

    [Fact]
    public async Task Connecting_IsNotAnonymous()
    {
        var response = await GoogleFlow.Client(fixture).PostAsJsonAsync("/account/federated/corporativo/connect", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient ClientFor(Guid userId, string role)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestIssuerTokens.ForClient(fixture.Services, "buteco-painel", userId.ToString(), role));
        return client;
    }
}
