using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.Federated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O host real com um provedor Google configurado (chave <see cref="Key"/>) e o <see cref="FakeGoogle"/> no
/// <c>HttpClient</c> do cliente OIDC: nenhum pedido sai para a rede (design.md da change
/// provedores-federados-google, D11). O log do emissor é capturado.
/// </summary>
public class FederatedLoginFixture : LocalUsersFixture
{
    public const string Key = "corporativo";

    public FakeGoogle Google { get; } = new();

    public IdentityIssuerLogCapture Logs { get; } = new();

    /// <summary>O <c>HostedDomain</c> da entrada, ou nulo.</summary>
    protected virtual string? HostedDomain => null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Por UseSetting: o catálogo dos provedores é lido no registro, antes do Build().
        builder.UseSetting($"Identity:FederatedProviders:{Key}:Type", "google");
        builder.UseSetting($"Identity:FederatedProviders:{Key}:ClientId", FakeGoogle.ClientId);
        builder.UseSetting($"Identity:FederatedProviders:{Key}:ClientSecret", FakeGoogle.ClientSecret);
        if (HostedDomain is not null)
        {
            builder.UseSetting($"Identity:FederatedProviders:{Key}:HostedDomain", HostedDomain);
        }

        builder.ConfigureTestServices(services =>
        {
            services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handler =>
            {
                if (handler.Name?.StartsWith("OpenIddict.Client.SystemNetHttp", StringComparison.Ordinal) == true)
                {
                    handler.AdditionalHandlers.Insert(0, Google.CreateHandler());
                }
            }));
        });

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}

/// <summary>O mesmo host com o Google fora do ar desde o boot: o discovery nunca entrou no cache.</summary>
public sealed class UnreachableGoogleFixture : FederatedLoginFixture
{
    public UnreachableGoogleFixture() => Google.State = FakeGoogle.Availability.Down;
}

/// <summary>O mesmo host com o Google sem responder desde o boot.</summary>
public sealed class HangingGoogleFixture : FederatedLoginFixture
{
    public HangingGoogleFixture() => Google.State = FakeGoogle.Availability.Hanging;
}

/// <summary>O mesmo host, só para contar os pedidos de discovery desde o boot (o cache não foi tocado por outro caso).</summary>
public sealed class DiscoveryCountFixture : FederatedLoginFixture;

/// <summary>O mesmo host com <c>HostedDomain</c> <c>empresa.com</c>.</summary>
public sealed class HostedDomainFederatedLoginFixture : FederatedLoginFixture
{
    protected override string? HostedDomain => "empresa.com";
}

/// <summary>
/// O fluxo do navegador contra o duplo, no molde do <see cref="IdentityIssuerUserFlow"/>: o início pela rota,
/// o consentimento no duplo e o retorno com o código, o state e os cookies que o início gravou.
/// </summary>
public static class GoogleFlow
{
    public sealed record Started(HttpResponseMessage Response, string? Url, string? FlowCookie)
    {
        public IReadOnlyDictionary<string, string> Query =>
            QueryHelpers.ParseQuery(new Uri(Url!).Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
    }

    public sealed record Returned(HttpResponseMessage Response, IReadOnlyList<string> SetCookies)
    {
        public string Location => Response.Headers.Location?.OriginalString ?? "";

        public string? Cookie(string name) =>
            SetCookies.Select(header => header.Split(';')[0]).FirstOrDefault(pair => pair.StartsWith($"{name}=", StringComparison.Ordinal) && pair.Length > name.Length + 1);

        public string? SetCookieHeader(string name) =>
            SetCookies.FirstOrDefault(header => header.StartsWith($"{name}=", StringComparison.Ordinal));

        public IReadOnlyDictionary<string, string> Query =>
            QueryHelpers.ParseQuery(new Uri(new Uri("https://painel.test"), Location).Query)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
    }

    /// <summary>O navegador: sem cookies automáticos, sem seguir redirecionamento e sem o token do painel que o fixture anexa.</summary>
    public static HttpClient Client(FederatedLoginFixture fixture) => SignedInUser.Anonymous(fixture);

    /// <summary><c>POST /auth/federated/{chave}/start</c>, JSON e a origem do issuer.</summary>
    public static async Task<Started> StartAsync(HttpClient client, string? retorno = null, string key = FederatedLoginFixture.Key, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/auth/federated/{key}/start") { Content = JsonContent.Create(new { retorno }) };
        request.Headers.Add("Origin", origin ?? LocalUserSeed.IssuerOrigin);
        return Read(await client.SendAsync(request));
    }

    /// <summary><c>POST /auth/invitation/federated/{chave}/start</c> com o token do convite, JSON e a origem do issuer (#274).</summary>
    public static async Task<Started> StartInvitationAsync(HttpClient client, string? token, string key = FederatedLoginFixture.Key, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/auth/invitation/federated/{key}/start") { Content = JsonContent.Create(new { token }) };
        request.Headers.Add("Origin", origin ?? LocalUserSeed.IssuerOrigin);
        return Read(await client.SendAsync(request));
    }

    /// <summary>O aceite inteiro pelo provedor: o início com o token, o consentimento e o retorno (#274).</summary>
    public static async Task<Returned> AcceptInvitationAsync(FederatedLoginFixture fixture, string token, FakeGoogle.Account account)
    {
        var client = Client(fixture);
        var started = await StartInvitationAsync(client, token);
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        return await ReturnAsync(client, Consent(fixture, started, account), started.Query["state"], started.FlowCookie);
    }

    public static async Task<Started> Read(Task<HttpResponseMessage> pending) => Read(await pending);

    public static Started Read(HttpResponseMessage response)
    {
        string? url = null;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            url = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.GetProperty("url").GetString();
        }

        var flow = SetCookiesOf(response).Select(header => header.Split(';')[0])
            .FirstOrDefault(pair => pair.StartsWith("buteco-fluxo-externo.", StringComparison.Ordinal));
        return new Started(response, url, flow);
    }

    /// <summary>O consentimento no duplo: o código amarrado ao PKCE e ao nonce do pedido.</summary>
    public static string Consent(FederatedLoginFixture fixture, Started started, FakeGoogle.Account account) =>
        fixture.Google.Consent(started.Query["code_challenge"], started.Query["nonce"], account);

    /// <summary><c>GET /auth/federated/callback</c> com o código e o state, e o cookie do fluxo (ou outro, ou nenhum).</summary>
    public static async Task<Returned> ReturnAsync(HttpClient client, string? code, string state, string? flowCookie, string? error = null)
    {
        var query = new Dictionary<string, string?> { ["state"] = state };
        if (code is not null)
        {
            query["code"] = code;
            query["scope"] = "email profile openid";
        }

        if (error is not null)
        {
            query["error"] = error;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, QueryHelpers.AddQueryString("/auth/federated/callback", query));
        if (flowCookie is not null)
        {
            request.Headers.Add("Cookie", flowCookie);
        }

        var response = await client.SendAsync(request);
        return new Returned(response, SetCookiesOf(response));
    }

    /// <summary>O fluxo inteiro do login: início, consentimento e retorno.</summary>
    public static async Task<Returned> LoginAsync(FederatedLoginFixture fixture, FakeGoogle.Account account, string? retorno = null)
    {
        var client = Client(fixture);
        var started = await StartAsync(client, retorno);
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        return await ReturnAsync(client, Consent(fixture, started, account), started.Query["state"], started.FlowCookie);
    }

    public static FakeGoogle.Account NewAccount(string? email = null, bool verified = true) =>
        new($"google-{Guid.NewGuid():N}", email ?? LocalUserSeed.UniqueEmail("google"), verified);

    /// <summary>Grava o vínculo direto no banco, para os casos do login.</summary>
    public static async Task LinkAsync(IServiceProvider services, Guid userId, FakeGoogle.Account account)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserFederatedIdentities.Add(UserFederatedIdentity.Connect(userId, "https://accounts.google.com/", account.Subject, account.Email, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    public static IReadOnlyList<string> SetCookiesOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}
