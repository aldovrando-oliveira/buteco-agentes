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
/// Acordo com o emissor real (spec <c>issuer-token-validation</c>; design.md das changes
/// validacao-jwks-inbox-connectors, D9, e usuarios-locais-login-emissor, D5; convenção 11):
/// o token de usuário do fluxo real, pelo cliente público <c>buteco-painel</c> com o
/// recurso <c>buteco-connectors</c>, é aceito pelo <c>apps/connectors</c>, com as chaves
/// obtidas do <c>apps/api</c> pelo endereço interno, e continua aceito com o
/// <c>apps/api</c> inalcançável.
/// <para>
/// A recusa por <c>aud</c> cruzada continua provada com um cliente confidencial de teste
/// de outro nome, <c>teste-so-inbox</c>, fora do prefixo <c>buteco-</c> e só com o escopo
/// <c>buteco-inbox</c>. Até a #180 os dois tokens vinham de um <c>buteco-painel</c>
/// confidencial, que a sincronização do catálogo sobrescreveria para público.
/// </para>
/// </summary>
public class IssuerTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string InboxOnlyClientId = "teste-so-inbox";
    private const string InboxOnlyClientSecret = "segredo-do-cliente-so-inbox";

    [Fact]
    public async Task RealUserToken_IsAcceptedByTheConnectors_AndTheInboxOnlyTokenIsRefused()
    {
        var (api, connectors) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var connectorsToken = await IssuerUserFlow.AccessTokenAsync(
            api.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            () => WarmUpAsync(api),
            RoundTripFixture.ApiPublicBaseUrl,
            RoundTripFixture.AdminPassword,
            "openid offline_access buteco-connectors");
        var inboxOnlyToken = await InboxOnlyTokenAsync(api);

        var token = new JsonWebToken(connectorsToken);
        Assert.Equal("at+jwt", token.Typ);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(RoundTripFixture.ApiPublicBaseUrl + "/", token.Issuer);
        Assert.Equal("buteco-painel", token.GetPayloadValue<string>("client_id"));
        Assert.Equal(["buteco-connectors"], token.Audiences);
        Assert.Equal(["buteco-inbox"], new JsonWebToken(inboxOnlyToken).Audiences);

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

    private static async Task WarmUpAsync(WebApplicationFactory<ApiProgram> api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Users.Bootstrap.IUserBootstrapper>().RunAsync(CancellationToken.None);
    }

    /// <summary>Token de <c>client_credentials</c> do cliente de teste, só com <c>buteco-inbox</c>.</summary>
    private static async Task<string> InboxOnlyTokenAsync(WebApplicationFactory<ApiProgram> api)
    {
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            if (await applications.FindByClientIdAsync(InboxOnlyClientId) is null)
            {
                await applications.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = InboxOnlyClientId,
                    ClientSecret = InboxOnlyClientSecret,
                    ClientType = ClientTypes.Confidential,
                    Permissions =
                    {
                        Permissions.Endpoints.Token,
                        Permissions.GrantTypes.ClientCredentials,
                        Permissions.Prefixes.Scope + "buteco-inbox",
                    },
                });
            }
        }

        var response = await api.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = InboxOnlyClientId,
            [Parameters.ClientSecret] = InboxOnlyClientSecret,
            [Parameters.Scope] = "buteco-inbox",
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
