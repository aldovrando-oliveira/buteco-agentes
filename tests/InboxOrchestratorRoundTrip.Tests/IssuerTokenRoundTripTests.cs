using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Acordo com o emissor real (spec <c>issuer-token-validation</c>; design.md da change
/// validacao-jwks-inbox-connectors, D9; convenção 11): o token que o <c>apps/api</c> emite
/// por <c>client_credentials</c> é aceito pelo <c>apps/inbox</c>, com as chaves obtidas do
/// <c>apps/api</c> pelo endereço interno, e continua aceito com o <c>apps/api</c>
/// inalcançável. Prova com o artefato real o que os testes do <c>apps/inbox</c> provam
/// contra o <c>TestIssuer</c>: <c>typ</c>, <c>kid</c>, <c>iss</c> com barra final,
/// <c>client_id</c> e <c>aud</c>.
/// <para>
/// O cliente confidencial <c>buteco-painel</c> existe só aqui, cadastrado depois do
/// <c>MigrateAsync</c>; em produção a #180 o cadastra público.
/// </para>
/// </summary>
public class IssuerTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string PanelClientId = "buteco-painel";
    private const string PanelClientSecret = "segredo-do-painel-de-teste";
    private const string InboxScope = "teste.inbox";
    private const string InboxResource = "buteco-inbox";

    [Fact]
    public async Task RealIssuerToken_IsAcceptedByTheInbox_AndStillAcceptedWithTheApiUnreachable()
    {
        await EnsurePanelClientAsync();
        var accessToken = await RequestTokenAsync();

        var token = new JsonWebToken(accessToken);
        Assert.Equal("at+jwt", token.Typ);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(RoundTripFixture.ApiPublicBaseUrl + "/", token.Issuer);
        Assert.Equal(PanelClientId, token.GetPayloadValue<string>("client_id"));
        Assert.Contains(InboxResource, token.Audiences);

        var inbox = fixture.InboxFactory.CreateClient();
        inbox.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // A primeira busca em segundo plano pode ter acontecido antes de o anel de chaves
        // do apps/api existir; o 503 é o "ainda sem chave", e a próxima tentativa resolve.
        var first = await PollAsync(() => inbox.GetAsync("/channels"), response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("/.well-known/openid-configuration", fixture.ApiIssuerKeys.PassedPaths);
        Assert.Contains("/.well-known/jwks", fixture.ApiIssuerKeys.PassedPaths);

        fixture.ApiIssuerKeys.Closed = true;
        try
        {
            var second = await inbox.GetAsync("/channels");
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }
        finally
        {
            fixture.ApiIssuerKeys.Closed = false;
        }
    }

    private async Task EnsurePanelClientAsync()
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        if (await scopes.FindByNameAsync(InboxScope) is null)
        {
            await scopes.CreateAsync(new OpenIddictScopeDescriptor { Name = InboxScope, Resources = { InboxResource } });
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
                    Permissions.Prefixes.Scope + InboxScope,
                },
            });
        }
    }

    private async Task<string> RequestTokenAsync()
    {
        var response = await fixture.ApiFactory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = PanelClientId,
            [Parameters.ClientSecret] = PanelClientSecret,
            [Parameters.Scope] = InboxScope,
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
