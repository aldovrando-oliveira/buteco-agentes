using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests;

// Tarefa 1.3: o RawText que MapGroup("/connectors") gera para as três rotas, lido
// do host construído, antes de escrever a tabela de subjects (a D6 da #102 achou
// barra final em MapGet("/")).
public class RoutePatternTests
{
    [Fact]
    public async Task RawTextDasRotasDoConector()
    {
        await using var factory = new ConnectorsFactory();
        _ = factory.CreateClient();

        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(pattern => pattern != "/health")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "/connectors/providers",
                "/connectors/providers/{providerKey}/folder",
                "/connectors/providers/{providerKey}/folders",
            ],
            patterns);
    }
}
