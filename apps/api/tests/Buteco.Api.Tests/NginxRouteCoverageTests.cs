using Buteco.Api.Identity;
using Buteco.Api.Tests.Support;
using Buteco.Tests.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

/// <summary>
/// Todo primeiro segmento que o apps/api serve tem bloco no nginx do stack, e todo prefixo do bloco do apps/api é
/// servido (spec <c>server-deployment</c>, "Nginx do stack roteia por prefixo"; change nginx-prefixo-applications,
/// #268). As rotas vêm do host construído, e o nginx.conf, do repositório. Foi a falta deste teste que deixou o
/// <c>/applications</c> da #186 cair no fallback do SPA na homologação, com 200 e HTML.
/// </summary>
[Collection(ApiHostCompositionCollection.Name)]
public class NginxRouteCoverageTests(ApiFactoryFixture factory)
{
    // As exceções, escritas aqui à parte a partir da spec, e não lidas do nginx.conf nem das rotas: um guarda que
    // lesse a mesma fonte do código não testaria a fonte (convenção 15, sexta forma).
    private static readonly Dictionary<string, string> ServedWithoutBlock = new(StringComparer.Ordinal)
    {
        ["health"] = "os healthchecks rodam dentro do contêiner, contra o próprio processo, e não atravessam o nginx",
        ["sync"] = "só o apps/connectors chama, pela rede interna do compose; não pode ser alcançável pelo domínio público",
    };

    private static readonly Dictionary<string, string> ExactBlocks = new(StringComparer.Ordinal)
    {
        [IdentityIssuerEndpoints.Configuration] =
            "atendido pelo OpenIddict sem rota mapeada; /.well-known/ é prefixo de uso geral na web e não entra inteiro",
        [IdentityIssuerEndpoints.JsonWebKeySet] =
            "atendido pelo OpenIddict sem rota mapeada; /.well-known/ é prefixo de uso geral na web e não entra inteiro",
        [IdentityIssuerEndpoints.Authorization] =
            "navegação de browser por definição, e por isso sem o desvio de navegação; o location = vence a regex de connect",
        [IdentityIssuerEndpoints.EndSession] =
            "navegação de browser por definição, e por isso sem o desvio de navegação; o location = vence a regex de connect",
    };

    [Fact]
    public void EveryServedPrefixHasABlock_AndEveryBlockPrefixIsServed()
    {
        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        var problems = NginxRouting.FindProblems(
            patterns,
            NginxRouting.ParseFile(),
            upstream: "api",
            ServedWithoutBlock,
            ExactBlocks,
            requiredUris: IdentityIssuerEndpoints.Expected.Keys);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
