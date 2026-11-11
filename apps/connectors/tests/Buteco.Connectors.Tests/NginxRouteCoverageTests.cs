using Buteco.Connectors.Tests.Support;
using Buteco.Tests.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Todo primeiro segmento que o apps/connectors serve tem bloco no nginx do stack, e todo prefixo do bloco do
/// apps/connectors é servido (spec <c>server-deployment</c>, "Nginx do stack roteia por prefixo"; change
/// nginx-prefixo-applications, #268). O host é construído aqui, como no <see cref="RoutePatternTests"/>: o
/// apps/connectors não tem banco, e a fixture não custa contêiner.
/// </summary>
public class NginxRouteCoverageTests
{
    // Escritas aqui à parte a partir da spec, e não lidas do nginx.conf nem das rotas (convenção 15, sexta forma).
    private static readonly Dictionary<string, string> ServedWithoutBlock = new(StringComparer.Ordinal)
    {
        ["health"] = "os healthchecks rodam dentro do contêiner, contra o próprio processo, e não atravessam o nginx",
    };

    private static readonly Dictionary<string, string> ExactBlocks = new(StringComparer.Ordinal);

    [Fact]
    public async Task EveryServedPrefixHasABlock_AndEveryBlockPrefixIsServed()
    {
        await using var factory = new ConnectorsFactory();
        _ = factory.CreateClient();

        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        var problems = NginxRouting.FindProblems(patterns, NginxRouting.ParseFile(), upstream: "connectors", ServedWithoutBlock, ExactBlocks);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
