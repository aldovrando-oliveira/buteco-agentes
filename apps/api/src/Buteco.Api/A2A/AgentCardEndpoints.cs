using global::A2A;
using Buteco.Api.Auth;
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
        // agente-enderecos-a2a, D1). Sem URL pública configurada, o endereço
        // fica vazio aqui como sempre ficou — corrigir isso mudaria o contrato
        // do card, que é outra change.
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
                    Url = addresses?.Url ?? string.Empty,
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
            // Descoberta pública, mas o endpoint A2A do agente exige
            // Bearer token — declarado via o mecanismo nativo da spec A2A
            // (SecuritySchemes/SecurityRequirements), não por omissão
            // (design.md, Decision 6).
            SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["bearer"] = new SecurityScheme
                {
                    HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "Bearer" },
                },
            },
            SecurityRequirements =
            [
                new SecurityRequirement
                {
                    Schemes = new Dictionary<string, StringList>
                    {
                        ["bearer"] = new StringList { List = [] },
                    },
                },
            ],
        };

        return TypedResults.Ok(card);
    }
}
