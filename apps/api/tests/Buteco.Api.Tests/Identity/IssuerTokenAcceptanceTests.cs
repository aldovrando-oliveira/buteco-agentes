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
/// O <c>apps/api</c> aceita o próprio token ao lado do HMAC (spec <c>route-authentication</c>,
/// "apps/api aceita o access token do emissor ao lado do HMAC"; spec <c>oidc-issuer</c>, "O
/// emissor convive com o login e a validação HMAC sem mudá-los"; design.md da change
/// usuarios-locais-login-emissor, D3.3).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 7.2:
/// <list type="bullet">
/// <item>com o subject tirado do <c>sub</c>, e não da tabela, <see cref="ForgedOperatorSub_IsNotTheOperator"/> reprova;</item>
/// <item>com um seletor que tenta os dois esquemas, <see cref="InvalidJwt_Is401_WithoutCallingTheHmacValidator"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class IssuerTokenAcceptanceTests(IssuerTokenAcceptanceTests.Fixture factory) : IClassFixture<IssuerTokenAcceptanceTests.Fixture>
{
    /// <summary>Conta as validações HMAC, para provar que o JWT inválido não chega lá.</summary>
    public sealed class CountingTokenService(ITokenService inner) : ITokenService
    {
        public int ValidateCalls;

        public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime) => inner.Issue(subject, lifetime);

        public TokenValidationResult Validate(string token)
        {
            Interlocked.Increment(ref ValidateCalls);
            return inner.Validate(token);
        }
    }

    public sealed class Fixture : LocalUsersFixture
    {
        public CountingTokenService? Counter { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(provider =>
                    Counter = new CountingTokenService(ActivatorUtilities.CreateInstance<TokenService>(provider)));
            });
        }
    }

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

    [Fact]
    public async Task HmacToken_IsStillAccepted_AndAnInvalidOneStill401()
    {
        var hmac = factory.Services.GetRequiredService<ITokenService>().Issue(ServiceScopeAuthorizationHandler.OperatorSubject, TimeSpan.FromMinutes(5)).Token;

        Assert.Equal(HttpStatusCode.OK, (await GetAgentsAsync(hmac)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAgentsAsync("payload.assinatura-errada")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAgentsAsync(null)).StatusCode);
    }

    [Fact]
    public async Task InvalidJwt_Is401_WithoutCallingTheHmacValidator()
    {
        var valid = await UserTokenAsync();
        var parts = valid.Split('.');
        var forged = $"{parts[0]}.{parts[1]}.{parts[2][..^4]}{(parts[2].EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA")}";
        _ = factory.Services.GetRequiredService<ITokenService>();
        var before = factory.Counter!.ValidateCalls;

        var forgedResponse = await GetAgentsAsync(forged);
        var garbage = await GetAgentsAsync("a.b.c");

        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
        Assert.Equal(before, factory.Counter.ValidateCalls);
    }
}
