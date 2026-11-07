using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O <c>apps/api</c> aceita só o próprio token (spec <c>route-authentication</c>, "apps/api
/// aceita só o access token do emissor"; spec <c>oidc-issuer</c>, "O access token do emissor é
/// a credencial das rotas do apps/api"; design.md das changes usuarios-locais-login-emissor,
/// D3.3, e remover-hmac-token-servico, D1 e D3).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o subject tirado do <c>sub</c>, e não da
/// tabela, <see cref="ForgedOperatorSub_IsNotTheOperator"/> reprova (registro no tasks.md da
/// change usuarios-locais-login-emissor, 7.2). Os dois casos da remoção do token antigo foram
/// vistos reprovar na árvore de antes (tasks.md da change remover-hmac-token-servico, 4.6).
/// </para>
/// </summary>
public class IssuerTokenAcceptanceTests(IssuerTokenAcceptanceTests.Fixture factory) : IClassFixture<IssuerTokenAcceptanceTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Password = "senha-certa-do-teste";
    private const string ClientSecret = "segredo-do-cliente";

    private HttpClient Anonymous()
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private async Task<HttpResponseMessage> GetAgentsAsync(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/agents");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await Anonymous().SendAsync(request);
    }

    private async Task<string> UserTokenAsync(string scope = IdentityIssuerUserFlow.AllScopes)
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var email = LocalUserSeed.UniqueEmail("aceita");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        var client = Anonymous();
        var cookie = await IdentityIssuerUserFlow.SessionCookieAsync(client, email, Password);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce, scope), cookie);
        return (await IdentityIssuerUserFlow.ReadTokensAsync(
            await IdentityIssuerUserFlow.RedeemAsync(client, IdentityIssuerUserFlow.CodeFrom(authorize), pkce.Verifier))).AccessToken;
    }

    /// <summary>Um cliente confidencial de teste, fora da tabela, com o escopo <c>buteco-api</c>.</summary>
    private async Task<string> ClientCredentialsTokenAsync(string clientId)
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(clientId) is null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientSecret = ClientSecret,
                    ClientType = ClientTypes.Confidential,
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + IdentityClients.ApiScope,
                    },
                });
            }
        }

        var response = await Anonymous().PostAsync("/connect/token",
            IdentityIssuerTestClient.TokenRequest(clientId: clientId, clientSecret: ClientSecret, scope: IdentityClients.ApiScope));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
    }

    [Fact]
    public async Task PanelToken_IsAcceptedOnAnApiRoute()
    {
        Assert.Equal(HttpStatusCode.OK, (await GetAgentsAsync(await UserTokenAsync())).StatusCode);
    }

    [Fact]
    public async Task TokenWithoutTheApiAudience_Is401()
    {
        var token = await UserTokenAsync(scope: $"openid offline_access {IdentityClients.InboxScope}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAgentsAsync(token)).StatusCode);
    }

    [Fact]
    public async Task ClientOutsideTheTable_Is403()
    {
        var token = await ClientCredentialsTokenAsync("teste-fora-da-tabela");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAgentsAsync(token)).StatusCode);
    }

    [Fact]
    public async Task ForgedOperatorSub_IsNotTheOperator()
    {
        // client_credentials põe sub = client_id: um cliente chamado "operator" tem o sub
        // do operador, e continua fora da tabela.
        var token = await ClientCredentialsTokenAsync(ServiceScopeAuthorizationHandler.OperatorSubject);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAgentsAsync(token)).StatusCode);
    }

    // Sem token, a rota que existe e a que não existe respondem igual: 401, corpo vazio e o
    // desafio Bearer (remover-hmac-token-servico, D3).
    [Fact]
    public async Task UnknownRoute_WithoutToken_Is401_WithTheBearerChallenge()
    {
        var unknown = await Anonymous().GetAsync("/rota-que-nao-existe");
        var known = await GetAgentsAsync(null);

        foreach (var response in new[] { unknown, known })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    // O token que os apps aceitavam até a #182 (remover-hmac-token-servico, D1): um ponto só,
    // assinatura simétrica com a chave das fixtures antigas.
    [Fact]
    public async Task HmacShapedToken_SignedWithTheOldKey_Is401()
    {
        var response = await GetAgentsAsync(OldSharedKeyToken.Sign());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    // O painel separa os dois 401 pelo corpo (design.md da change minha-conta-painel, D10;
    // decisão do dono Q0): o 401 com application/problem+json é a recusa da própria rota (a
    // senha ou o código errado), e volta à tela; o 401 de token, sem corpo, faz o
    // apps/frontend/src/auth/sessionFetch.ts renovar e repetir. Um corpo problem+json aqui
    // faria o painel parar de renovar no token vencido. A #247 troca o critério por um campo
    // de tipo.
    [Theory]
    [InlineData(null)]
    [InlineData("nao.e.um.token")]
    public async Task TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis(string? token)
    {
        var response = await GetAgentsAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
