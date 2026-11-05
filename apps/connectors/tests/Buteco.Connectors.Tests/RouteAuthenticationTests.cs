using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Tests;

// connectors-scaffold: "Token do emissor validado localmente",
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
