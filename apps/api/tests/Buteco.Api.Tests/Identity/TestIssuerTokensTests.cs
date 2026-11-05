using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Validation;
using Buteco.Api.Tests.Support;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O helper de teste que as fixtures usam no lugar do token antigo de operador (design.md da
/// change fixtures-token-emissor, D1): o token do painel tem a forma de um token do fluxo
/// real, passa no catálogo e recebe <c>403</c> nas rotas de <c>CurrentUser</c>, porque o
/// <c>sub</c> não é usuário; o de um cliente de serviço fica nas rotas da lista dele.
/// </summary>
public class TestIssuerTokensTests(TestIssuerTokensTests.Fixture factory) : IClassFixture<TestIssuerTokensTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    [Fact]
    public async Task PanelToken_HasTheShapeOfARealPanelToken()
    {
        var real = new JsonWebToken((await SignedInUser.CreateAsync(factory, "forma-painel")).AccessToken);
        var panel = new JsonWebToken(TestIssuerTokens.Panel(factory.Services)!);

        Assert.Equal(real.Typ, panel.Typ);
        Assert.Equal(real.Alg, panel.Alg);
        Assert.Equal(real.Kid, panel.Kid);
        Assert.Equal(real.Issuer, panel.Issuer);
        Assert.Equal(IdentityClients.PanelClientId, panel.GetPayloadValue<string>(Claims.ClientId));
        Assert.Equal(TestIssuerTokens.PanelSubject, panel.Subject);
        Assert.Equal([IdentityClients.ApiScope], panel.Audiences);
        // Os mesmos nomes de claims do fluxo real, com uma exceção só: o oi_au_id, que é a
        // autorização (a sessão) do usuário. O token da fixture não tem usuário nem sessão, e
        // quem lê o oi_au_id são as rotas de sessão da conta, que já respondem 403 a ele
        // (design.md, D1, correção na implementação).
        var expected = ClaimNames(real).Where(name => name != IssuerTokenAuthentication.AuthorizationIdClaim);
        Assert.True(expected.SequenceEqual(ClaimNames(panel)),
            $"fluxo real: {Base64UrlEncoder.Decode(real.EncodedPayload)}\nhelper: {Base64UrlEncoder.Decode(panel.EncodedPayload)}");
    }

    [Fact]
    public async Task PanelToken_PassesTheCatalog_AndIs403OnTheCurrentUserRoutes()
    {
        // Com um usuário no banco: o 403 vem de o sub não ser usuário, e não de não haver nenhum.
        await SignedInUser.CreateAsync(factory, "existe-usuario");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestIssuerTokens.Panel(factory.Services));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/account")).StatusCode);
    }

    [Fact]
    public async Task ServiceClientToken_StaysOnItsRoutes()
    {
        var panel = factory.CreateClient();
        panel.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestIssuerTokens.Panel(factory.Services));
        var created = await panel.PostAsJsonAsync(
            "/agents",
            new { name = "Agente", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var agentId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var inbox = factory.CreateClient();
        inbox.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestIssuerTokens.ForClient(factory.Services, IdentityClients.InboxServiceClientId, IdentityClients.InboxServiceClientId));

        Assert.Equal(HttpStatusCode.OK, (await inbox.GetAsync($"/agents/{agentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inbox.GetAsync("/agents")).StatusCode);
    }

    private static IEnumerable<string> ClaimNames(JsonWebToken token) =>
        JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload)).RootElement
            .EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
