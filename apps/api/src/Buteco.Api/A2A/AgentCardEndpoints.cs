using global::A2A;
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
        app.MapGet("/agents/{id:guid}/.well-known/agent-card.json", GetAgentCardAsync);

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

        var baseUrl = publicUrlOptions.Value.BaseUrl.TrimEnd('/');

        var card = new AgentCard
        {
            Name = agent.Name,
            Description = agent.Description ?? string.Empty,
            Version = "1.0.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = $"{baseUrl}/agents/{agent.Id}/a2a",
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
        };

        return TypedResults.Ok(card);
    }
}
