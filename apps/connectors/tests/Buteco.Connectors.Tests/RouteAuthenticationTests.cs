using System.Net;
using System.Net.Http.Headers;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Tests;

// connectors-scaffold: "Token validado localmente com a chave compartilhada",
// "Health check anônimo e classificado" e "CORS para o frontend".
public class RouteAuthenticationTests
{
    [Fact]
    public async Task RotaSemToken_Responde401()
    {
        await using var factory = new ConnectorsFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/connectors/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenDeOperadorAssinadoComAMesmaChave_Responde200()
    {
        await using var factory = new ConnectorsFactory();
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.GetAsync("/connectors/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TokenAssinadoComOutraChave_Responde401()
    {
        await using var factory = new ConnectorsFactory();
        var otherService = new TokenService(Microsoft.Extensions.Options.Options.Create(new TokenSigningOptions { TokenSigningKey = "outra-chave" }));
        var (token, _) = otherService.Issue("operator", TimeSpan.FromMinutes(5));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/connectors/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenExpirado_Responde401()
    {
        await using var factory = new ConnectorsFactory();
        var token = TestAuthentication.IssueToken(factory.Services, "operator", TimeSpan.FromSeconds(-1));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/connectors/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BootComChaveDeAssinaturaVazia_Falha(string key)
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?> { ["Auth:TokenSigningKey"] = key });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Auth:TokenSigningKey", Flatten(exception));
    }

    // Ausente de verdade: em Production não há appsettings.Development.json, e a
    // fábrica não injeta a chave quando o valor é nulo.
    [Fact]
    public void BootSemChaveDeAssinatura_Falha()
    {
        using var factory = new ConnectorsFactory(
            extraConfiguration: new Dictionary<string, string?> { ["Auth:TokenSigningKey"] = null },
            environment: "Production");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Auth:TokenSigningKey", Flatten(exception));
    }

    [Fact]
    public void RotaAnonimaSemClassificacao_DerrubaOBoot()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/health", () => "ok").AllowAnonymous();

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateRouteAuthenticationClassification("/health"));

        Assert.Contains("/health", exception.Message);
    }

    [Fact]
    public async Task PreflightDeOrigemConfigurada_TrazAllowOrigin()
    {
        await using var factory = new ConnectorsFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("http://painel.test"));

        Assert.Equal("http://painel.test", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task PreflightDeOrigemNaoConfigurada_NaoTrazAllowOrigin()
    {
        await using var factory = new ConnectorsFactory();
        var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight("http://intruso.test"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/connectors/providers");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    internal static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
