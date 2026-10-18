using Buteco.Workers.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Tests.Mcp;

/// <summary>
/// Guarda da redação de header no log do cliente HTTP da execução MCP (change
/// mcp-autenticacao-header-customizado, D8): o mesmo do <c>apps/api</c>. Sem
/// <c>RedactLoggedHeaders</c>, todo valor de header sai como <c>*</c>; com uma
/// lista, os nomes de fora dela — o X-API-TOKEN do tipo CustomHeader, por
/// exemplo — passam a sair em claro em <c>Trace</c> (design.md, V5).
///
/// <para>
/// Exercita o REGISTRO DE PRODUÇÃO, <see cref="McpTransportFactory.AddHttpClient"/>,
/// que é o que o <c>Program.cs</c> chama — mesmo caminho do guarda do timeout de
/// conexão (change timeout-de-conexao-saida-workers, D3). <c>apps/workers</c> não
/// tem um host de teste montado a partir do <c>Program.cs</c>; o que este teste
/// NÃO prova é um <c>ConfigureHttpClientDefaults</c> acrescentado direto no
/// <c>Program.cs</c>.
/// </para>
/// </summary>
public class HttpClientHeaderRedactionTests
{
    [Theory]
    [InlineData("X-API-TOKEN")]
    [InlineData("Authorization")]
    public void McpToolExecutionClient_RedactsHeaderValuesInLogs(string headerName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        McpTransportFactory.AddHttpClient(services);

        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(McpTransportFactory.HttpClientName);

        Assert.True(options.ShouldRedactHeaderValue(headerName));
    }
}
