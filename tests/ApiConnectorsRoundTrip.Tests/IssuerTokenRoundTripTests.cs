extern alias ApiAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ApiConnectorsRoundTrip.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using ApiProgram = ApiAssembly::Program;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// Acordo com o emissor real (spec <c>issuer-token-validation</c>; design.md da change
/// validacao-jwks-inbox-connectors, D9; convenção 11): o token que o <c>apps/api</c> emite
/// para <c>buteco-painel</c> com o recurso <c>buteco-connectors</c> é aceito pelo
/// <c>apps/connectors</c>, com as chaves obtidas do <c>apps/api</c> pelo endereço interno,
/// e continua aceito com o <c>apps/api</c> inalcançável; o token só com o recurso
/// <c>buteco-inbox</c> é recusado.
/// <para>
/// O cliente confidencial <c>buteco-painel</c> existe só aqui, cadastrado depois do
/// <c>MigrateAsync</c>; em produção a #180 o cadastra público.
/// </para>
/// </summary>
public class IssuerTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string PanelClientId = "buteco-painel";
    private const string PanelClientSecret = "segredo-do-painel-de-teste";
    private const string ConnectorsScope = "teste.connectors";
    private const string InboxScope = "teste.inbox";

    [Fact]
    public async Task RealIssuerToken_IsAcceptedByTheConnectors_AndTheInboxOnlyTokenIsRefused()
    {
        var (api, connectors) = fixture.Build(RoundTripFixture.TokenSigningKey);
        await EnsurePanelClientAsync(api);
        var connectorsToken = await RequestTokenAsync(api, ConnectorsScope);
        var inboxOnlyToken = await RequestTokenAsync(api, InboxScope);

        var token = new JsonWebToken(connectorsToken);
        Assert.Equal("at+jwt", token.Typ);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(RoundTripFixture.ApiPublicBaseUrl + "/", token.Issuer);
        Assert.Equal(PanelClientId, token.GetPayloadValue<string>("client_id"));
        Assert.Equal(["buteco-connectors"], token.Audiences);

        var client = connectors.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connectorsToken);

        // A primeira busca em segundo plano pode ter acontecido antes de o anel de chaves
        // do apps/api existir; o 503 é o "ainda sem chave", e a próxima tentativa resolve.
        var first = await PollAsync(() => client.GetAsync("/connectors/providers"), response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("/.well-known/jwks", fixture.ApiIssuerKeys.PassedPaths);

        var inboxOnly = connectors.CreateClient();
        inboxOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", inboxOnlyToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await inboxOnly.GetAsync("/connectors/providers")).StatusCode);

        fixture.ApiIssuerKeys.Closed = true;
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/connectors/providers")).StatusCode);
        }
        finally
        {
            fixture.ApiIssuerKeys.Closed = false;
        }
    }

    private static async Task EnsurePanelClientAsync(WebApplicationFactory<ApiProgram> api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var (name, resource) in new[] { (ConnectorsScope, "buteco-connectors"), (InboxScope, "buteco-inbox") })
        {
            if (await scopes.FindByNameAsync(name) is null)
            {
                await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = name, Resources = { resource } });
            }
        }

        if (await applications.FindByClientIdAsync(PanelClientId) is null)
        {
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = PanelClientId,
                ClientSecret = PanelClientSecret,
                ClientType = ClientTypes.Confidential,
                Permissions =
                {
                    Permissions.Endpoints.Token,
                    Permissions.GrantTypes.ClientCredentials,
                    Permissions.Prefixes.Scope + ConnectorsScope,
                    Permissions.Prefixes.Scope + InboxScope,
                },
            });
        }
    }

    private static async Task<string> RequestTokenAsync(WebApplicationFactory<ApiProgram> api, string scope)
    {
        var response = await api.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = PanelClientId,
            [Parameters.ClientSecret] = PanelClientSecret,
            [Parameters.Scope] = scope,
        }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
    }

    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await send();
            if (done(response) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(200);
        }
    }
}
