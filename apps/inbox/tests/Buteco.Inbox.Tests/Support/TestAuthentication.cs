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

    public static Dictionary<string, string?> ConfigOverrides { get; } = new()
    {
        ["Auth:TokenSigningKey"] = TokenSigningKey,
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

    public static void AttachOperatorToken(HttpClient client, IServiceProvider services)
    {
        var tokenService = services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue("operator", TimeSpan.FromMinutes(30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
