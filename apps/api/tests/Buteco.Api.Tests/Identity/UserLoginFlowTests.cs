using System.Net;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O fluxo authorization code + PKCE do painel, sem navegador (spec <c>issuer-user-login</c>,
/// "Fluxo authorization code com PKCE obrigatório"; design.md da change
/// usuarios-locais-login-emissor, D1 e D5).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.8:
/// <list type="bullet">
/// <item>sem <c>RequireProofKeyForCodeExchange</c>, <see cref="WithoutCodeChallenge_NoCodeIsIssued"/> reprova;</item>
/// <item>com o <c>retorno</c> montado por <c>GetDisplayUrl()</c>, <see cref="WithoutSession_TheReturnIsTheRelativeRequest_NeverTheHost"/> reprova.</item>
/// </list>
/// </para>
/// </summary>
public class UserLoginFlowTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => IdentityIssuerUserFlow.Client(factory);

    private async Task<(Buteco.Api.Users.User User, string Cookie)> SessionAsync(string prefix)
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var email = LocalUserSeed.UniqueEmail(prefix);
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, Password, name: "Maria Teste");
        return (user, await IdentityIssuerUserFlow.SessionCookieAsync(Client(), email, Password));
    }

    [Fact]
    public async Task FullFlow_IssuesTokensForTheThreeApps()
    {
        var (user, cookie) = await SessionAsync("fluxo");
        var client = Client();
        var pkce = IdentityIssuerUserFlow.Pkce.Create();

        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce), cookie);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith(IdentityIssuerUserFlow.RedirectUri, authorize.Headers.Location!.AbsoluteUri);

        var tokens = await IdentityIssuerUserFlow.ReadTokensAsync(
            await IdentityIssuerUserFlow.RedeemAsync(client, IdentityIssuerUserFlow.CodeFrom(authorize), pkce.Verifier));

        var jwks = new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks"));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(tokens.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = ApiFactoryFixture.PublicBaseUrl + "/",
            ValidAudiences = IdentityClients.Scopes,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidTypes = ["at+jwt"],
            // O relógio do app é o FakeTimeProvider, parado no início da classe.
            ValidateLifetime = false,
        });
        Assert.True(validation.IsValid, validation.Exception?.ToString());

        var access = new JsonWebToken(tokens.AccessToken);
        Assert.Equal(user.Id.ToString(), access.Subject);
        Assert.Equal("buteco-painel", access.GetPayloadValue<string>(Claims.ClientId));
        Assert.Equal(IdentityClients.Scopes.Order(), access.Audiences.Order());
        Assert.Equal("at+jwt", access.Typ);
        Assert.Equal(TimeSpan.FromMinutes(15), access.ValidTo - access.IssuedAt);

        var id = new JsonWebToken(tokens.IdToken);
        Assert.Equal(user.Email, id.GetPayloadValue<string>(Claims.Email));
        Assert.Equal("Maria Teste", id.GetPayloadValue<string>(Claims.Name));

        // O carimbo de segurança não sai em token que o cliente lê.
        Assert.False(access.TryGetPayloadValue<string>("buteco_stamp", out _));
        Assert.False(id.TryGetPayloadValue<string>("buteco_stamp", out _));
    }

    [Fact]
    public async Task WithoutSession_RedirectsToTheLoginScreen_AndIssuesNoCode()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);

        var response = await IdentityIssuerUserFlow.AuthorizeAsync(
            Client(), IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/entrar?retorno=", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("code=", response.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task WithoutSession_TheReturnIsTheRelativeRequest_NeverTheHost()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);
        var url = IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create());
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Host = "malicioso.exemplo";

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        var retorno = QueryHelpers.ParseQuery(location[location.IndexOf('?')..])["retorno"].ToString();
        Assert.StartsWith("/connect/authorize?", retorno);
        Assert.Equal(url, retorno);
        Assert.DoesNotContain("://", retorno);
        Assert.DoesNotContain("malicioso", retorno);
    }

    [Fact]
    public async Task PromptNone_WithoutSession_ReturnsLoginRequired()
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(factory.Services);

        var response = await IdentityIssuerUserFlow.AuthorizeAsync(
            Client(), IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create(), prompt: PromptValues.None), cookie: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(IdentityIssuerUserFlow.RedirectUri, response.Headers.Location!.AbsoluteUri);
        Assert.Equal(Errors.LoginRequired, QueryHelpers.ParseQuery(response.Headers.Location.Query)[Parameters.Error].ToString());
    }

    [Fact]
    public async Task WithoutCodeChallenge_NoCodeIsIssued()
    {
        var (_, cookie) = await SessionAsync("sem-pkce");

        var response = await IdentityIssuerUserFlow.AuthorizeAsync(Client(), IdentityIssuerUserFlow.AuthorizeUrl(pkce: null), cookie);

        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.DoesNotContain("code=", location);
    }

    [Fact]
    public async Task WrongVerifier_IsInvalidGrant()
    {
        var (_, cookie) = await SessionAsync("verifier");
        var client = Client();
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce), cookie);

        var response = await IdentityIssuerUserFlow.RedeemAsync(client, IdentityIssuerUserFlow.CodeFrom(authorize), IdentityIssuerUserFlow.Pkce.Create().Verifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Errors.InvalidGrant, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnregisteredRedirectUri_IsRefused_WithoutRedirecting()
    {
        var (_, cookie) = await SessionAsync("redirect");

        var response = await IdentityIssuerUserFlow.AuthorizeAsync(
            Client(), IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create(), redirectUri: "https://atacante.exemplo/volta"), cookie);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task PasswordGrant_IsUnsupported()
    {
        var response = await Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.Password,
            [Parameters.ClientId] = "buteco-painel",
            [Parameters.Username] = "x",
            [Parameters.Password] = "y",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Errors.UnsupportedGrantType, await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// O authorize antes da primeira sincronização do catálogo (spec <c>issuer-user-login</c>,
/// "Authorize antes da primeira sincronização"; design.md D3.13): <c>400</c>
/// <c>invalid_request</c>, sem código e sem redirecionar, e o gate não espera o catálogo.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o gate exigindo o catálogo sincronizado
/// além do anel, <c>/health</c> responde <c>503</c> e o teste reprova (registro no
/// tasks.md, 6.8).
/// </para>
/// </summary>
public class AuthorizeBeforeCatalogTests(AuthorizeBeforeCatalogTests.Fixture factory) : IClassFixture<AuthorizeBeforeCatalogTests.Fixture>
{
    public sealed class NeverSyncedCatalog : IIdentityClientCatalog
    {
        public Task SyncAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("catálogo indisponível no teste");
    }

    public sealed class Fixture : LocalUsersFixture
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdentityClientCatalog>();
                services.AddScoped<IIdentityClientCatalog, NeverSyncedCatalog>();
            });
        }
    }

    [Fact]
    public async Task Authorize_Is400InvalidRequest_WithoutRedirect_AndHealthIs200()
    {
        // O relógio do host é falso e parado, e o aquecimento não chega à nova tentativa:
        // quem carrega o anel aqui é o gate, no primeiro pedido. O catálogo nunca sincroniza.
        var client = IdentityIssuerUserFlow.Client(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.True(factory.Services.GetRequiredService<IdentityKeyRing>().IsLoaded);

        var response = await IdentityIssuerUserFlow.AuthorizeAsync(
            client, IdentityIssuerUserFlow.AuthorizeUrl(IdentityIssuerUserFlow.Pkce.Create()), cookie: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains(Errors.InvalidRequest, await response.Content.ReadAsStringAsync());
    }
}
