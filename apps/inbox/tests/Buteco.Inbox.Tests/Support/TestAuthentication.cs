using System.Net.Http.Headers;
using Buteco.Inbox.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests.Support;

// Config compartilhada pelas fixtures WebApplicationFactory<Program>
// deste projeto de teste (mesmo espírito de
// Buteco.Api.Tests.Support.TestAuthentication, apps/api).
public static class TestAuthentication
{
    // Mesmo valor literal usado por Buteco.Api.Tests.Support.TestAuthentication
    // (apps/api) — não compartilhado em código (projetos de teste
    // separados), mas precisa ser o mesmo texto para os testes cruzados
    // de tests/InboxOrchestratorRoundTrip.Tests (design.md, Risco 1).
    public const string TokenSigningKey = "test-signing-key-shared-between-api-and-inbox-fixtures";

    // O segredo do apps/inbox no emissor (servicos-token-emissor, D5), obrigatório no boot.
    // Gerado com `openssl rand -base64 32`; distinto do de desenvolvimento e da
    // Inbox:CredentialEncryptionKey das fixtures.
    public const string ServiceClientSecret = "43q+QRk0EcuewX/YFkpiAk65NfH62UGABTP0uZkGZok=";

    public static Dictionary<string, string?> ConfigOverrides { get; } = new()
    {
        ["Auth:TokenSigningKey"] = TokenSigningKey,
        ["ServiceClient:Secret"] = ServiceClientSecret,
    };

    // Identity:Issuer das fixtures (validacao-jwks-inbox-connectors, D7). Vazio desliga a
    // validação do token do emissor: sem isso, o appsettings.Development.json a ligaria
    // em toda fixture, com busca de chaves em segundo plano. Por UseSetting, e não em
    // ConfigOverrides: a WebApplicationFactory aplica o ConfigureAppConfiguration só no
    // Build(), e o AddIssuerTokenAuthentication decide o registro antes dele, lendo a
    // configuração do builder. Quem testa o emissor liga com o TestIssuer
    // (IssuerTokenFactory).
    public static IWebHostBuilder UseIssuer(this IWebHostBuilder builder, string issuer = "") =>
        builder.UseSetting("Identity:Issuer", issuer);

    /// <summary>
    /// Liga a validação do token do emissor contra <paramref name="issuer"/>, em memória: o
    /// <c>Identity:Issuer</c> dele e o handler dele como primário do cliente <c>IssuerKeys</c>,
    /// que lê o discovery e o JWKS pelo <c>Api:BaseUrl</c> do host (fixtures-token-emissor, D2).
    /// </summary>
    public static IWebHostBuilder UseTestIssuer(this IWebHostBuilder builder, TestIssuer issuer) =>
        builder.UseIssuer(TestIssuer.Issuer).ConfigureServices(services =>
            services.AddHttpClient(IssuerKeysRetriever.HttpClientName).ConfigurePrimaryHttpMessageHandler(issuer.CreateHandler));

    /// <summary>
    /// Espera a primeira obtenção das chaves. Sem isso, o primeiro pedido de um teste recebe o
    /// <c>503</c> de "chaves ainda não obtidas" (validacao-jwks-inbox-connectors, D4).
    /// </summary>
    public static Task WaitForIssuerKeysAsync(IServiceProvider services, TimeSpan timeout)
    {
        var state = services.GetRequiredService<IssuerKeysState>();
        return Poll.UntilAsync(() => state.Obtained, "as chaves do emissor de teste não foram obtidas", timeout);
    }

    // Anexado por padrão no ConfigureClient das fixtures: o token do painel, do emissor
    // de teste da fixture (fixtures-token-emissor, D2).
    public static void AttachPanelToken(HttpClient client, TestIssuer issuer) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Issue());
}
