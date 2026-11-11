using Buteco.Inbox.Tests.Support;
using Buteco.Tests.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Todo primeiro segmento que o apps/inbox serve tem bloco no nginx do stack, e todo prefixo do bloco do apps/inbox
/// é servido (spec <c>server-deployment</c>, "Nginx do stack roteia por prefixo"; change nginx-prefixo-applications,
/// #268). As rotas vêm do host construído, e o nginx.conf, do repositório: a suíte que reprova é a de quem criou a
/// rota, e não a homologação.
/// </summary>
[Collection(InboxHostCompositionCollection.Name)]
public class NginxRouteCoverageTests(InboxFactoryFixture factory)
{
    // Escritas aqui à parte a partir da spec, e não lidas do nginx.conf nem das rotas (convenção 15, sexta forma).
    private static readonly Dictionary<string, string> ServedWithoutBlock = new(StringComparer.Ordinal)
    {
        ["health"] = "os healthchecks rodam dentro do contêiner, contra o próprio processo, e não atravessam o nginx",
    };

    private static readonly Dictionary<string, string> ExactBlocks = new(StringComparer.Ordinal);

    [Fact]
    public void EveryServedPrefixHasABlock_AndEveryBlockPrefixIsServed()
    {
        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        var problems = NginxRouting.FindProblems(patterns, NginxRouting.ParseFile(), upstream: "inbox", ServedWithoutBlock, ExactBlocks);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
