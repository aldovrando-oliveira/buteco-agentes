using global::A2A;
using Buteco.Api.Auth;
using Buteco.Api.Identity;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.A2A;

// Descoberta externa do agente via AgentCard (protocolo A2A) — não usa
// AgentA2AServerRegistry nem A2AServer.GetExtendedAgentCardAsync/
// MapWellKnownAgentCard do SDK, nenhum dos dois compatível com N agentes
// por host (ver design.md, Decision 1). Lê o Agent fresco do banco a cada
// requisição, mesmo princípio de EnqueueingAgentHandler — não há cache para
// invalidar (Decision 2).
public static class AgentCardEndpoints
{
    public static IEndpointRouteBuilder MapAgentCardEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agents/{id:guid}/.well-known/agent-card.json", GetAgentCardAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.PublicDiscovery));

        return app;
    }

    private static async Task<Results<Ok<AgentCard>, NotFound>> GetAgentCardAsync(
        Guid id,
        IServiceScopeFactory scopeFactory,
        IOptions<PublicUrlOptions> publicUrlOptions,
        IdentityIssuerState issuerState,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agent = await dbContext.Agents.AsNoTracking()
            .FirstOrDefaultAsync(agent => agent.Id == id, cancellationToken);

        if (agent is null)
        {
            return TypedResults.NotFound();
        }

        // Mesmo ponto de montagem que a resposta de agente usa, para os dois
        // nunca anunciarem endereços diferentes (design.md da change
        // agente-enderecos-a2a, D1). A URL pública é obrigatória desde a change
        // usuarios-locais-login-emissor (D3.4).
        var addresses = AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id);

        var card = new AgentCard
        {
            Name = agent.Name,
            Description = agent.Description ?? string.Empty,
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = addresses.Url,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                },
            ],
            // Agente sem conceito formal de "organização operadora" hoje —
            // ver Decision 3 do design.md. Não confundir com Agent.Provider
            // (vendor de LLM), que não tem nenhuma relação com este campo.
            Provider = null,
            Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = true },
            Skills = AgentSkillMapper.MapSkills(agent.Skills).ToList(),
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            // Descoberta pública, mas o endpoint A2A do agente exige o access token do emissor do
            // Buteco, obtido por client_credentials — declarado pelo mecanismo nativo da spec A2A
            // (SecuritySchemes/SecurityRequirements), não por omissão (design.md da change
            // auth-login-e-servico, Decision 6). Até a #186 era o esquema HTTP "bearer", que não dizia
            // onde obter o token; desde a change aplicacoes-externas-credencial (D7) é o OAuth2 do
            // emissor. O issuer é o da url pública, que o boot exige ("Url pública obrigatória"): no
            // app que atende, ele nunca é nulo. Igual em todo agente, e sem dizer quais aplicações
            // têm acesso (a credencial é da aplicação, e o card é anônimo).
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                [OAuth2SchemeName] = OAuth2Scheme(issuerState.Issuer!),
            },
            SecurityRequirements =
            [
                new SecurityRequirement
                {
                    Schemes = new Dictionary<string, StringList>
                    {
                        [OAuth2SchemeName] = new StringList { List = [IdentityClients.ApiScope] },
                    },
                },
            ],
        };

        return TypedResults.Ok(card);
    }

    public const string OAuth2SchemeName = "oauth2";

    private static SecurityScheme OAuth2Scheme(Uri issuer) => new()
    {
        OAuth2SecurityScheme = new OAuth2SecurityScheme
        {
            Flows = new OAuthFlows
            {
                ClientCredentials = new ClientCredentialsOAuthFlow
                {
                    TokenUrl = new Uri(issuer, "connect/token").AbsoluteUri,
                    Scopes = new Dictionary<string, string>
                    {
                        [IdentityClients.ApiScope] = "Chamar agentes pelo A2A",
                    },
                },
            },
            OAuth2MetadataUrl = new Uri(issuer, ".well-known/openid-configuration").AbsoluteUri,
        },
    };
}
