using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Tests;

/// <summary>
/// Guarda da redação de header no log do cliente HTTP do MCP (change
/// mcp-autenticacao-header-customizado, D8). Desde o .NET 9, sem
/// <c>RedactLoggedHeaders</c>, TODO valor de header sai como <c>*</c> nos logs
/// de <c>Trace</c> do <c>IHttpClientFactory</c> (documentação oficial e
/// <c>Microsoft.Extensions.Http</c> 10.0.10 decompilado — design.md, V5). O
/// default se DESLIGA com qualquer <c>RedactLoggedHeaders</c> que liste nomes:
/// os de fora da lista passam a sair em claro, e o X-API-TOKEN do tipo
/// CustomHeader seria um deles.
///
/// <para>
/// Lê a opção efetiva do client nomeado na composição REAL de
/// <c>Program.cs</c> (pelo <see cref="ApiFactoryFixture"/>, que não toca este
/// client). É determinístico: não depende do nível de log do teste, que é o que
/// um guarda por varredura de linhas de log exigiria.
/// </para>
/// </summary>
public class HttpClientHeaderRedactionTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Theory]
    [InlineData("X-API-TOKEN")]
    [InlineData("Authorization")]
    public void McpConnectionTesterClient_RedactsHeaderValuesInLogs(string headerName)
    {
        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(McpConnectionTester.HttpClientName);

        Assert.True(options.ShouldRedactHeaderValue(headerName));
    }
}
