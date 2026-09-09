using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations;
using Buteco.Api.AgentKnowledgeBindings.Entities;
using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.AgentKnowledgeBindings.Commands.ReplaceAgentKnowledgeBases;

public sealed class ReplaceAgentKnowledgeBasesCommandHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<ReplaceAgentKnowledgeBasesCommand, ReplaceAgentKnowledgeBasesResult>
{
    public async ValueTask<ReplaceAgentKnowledgeBasesResult> Handle(ReplaceAgentKnowledgeBasesCommand command, CancellationToken cancellationToken)
    {
        // Sem filtro por IsActive nem aqui nem abaixo (design.md, D5): agente
        // inativo pode ter seus vínculos configurados, e base inativa é
        // vinculável. O filtro de estado pertence à resolução, em
        // apps/workers, no idioma de McpToolSetResolver.
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.AgentId, cancellationToken);

        if (agent is null)
        {
            return ReplaceAgentKnowledgeBasesResult.AgentNotFound();
        }

        // Dedup silenciosa (design.md, D4) — é o que os dois precedentes
        // fazem. PUT de substituição integral tem semântica de conjunto: quem
        // manda o mesmo id duas vezes está pedindo o mesmo estado final.
        var requestedIds = command.KnowledgeBaseIds.Distinct().ToList();

        var existingIds = await dbContext.KnowledgeBases
            .Where(knowledgeBase => requestedIds.Contains(knowledgeBase.Id))
            .Select(knowledgeBase => knowledgeBase.Id)
            .ToListAsync(cancellationToken);

        var invalidIds = requestedIds.Except(existingIds).ToList();
        if (invalidIds.Count > 0)
        {
            // Rejeição atômica: retorna antes de qualquer remoção ou adição,
            // então nem o payload é aplicado nem os vínculos anteriores são
            // perdidos.
            return ReplaceAgentKnowledgeBasesResult.InvalidIds(invalidIds);
        }

        var currentBindings = await dbContext.AgentKnowledgeBases
            .Where(binding => binding.AgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentKnowledgeBases.RemoveRange(currentBindings);

        foreach (var knowledgeBaseId in requestedIds)
        {
            dbContext.AgentKnowledgeBases.Add(new AgentKnowledgeBase(command.AgentId, knowledgeBaseId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);
        var knowledgeBases = await AgentKnowledgeBaseLookup.GetLinkedKnowledgeBasesAsync(dbContext, agent.Id, cancellationToken);

        return ReplaceAgentKnowledgeBasesResult.Success(AgentResponse.FromEntity(agent, mcpServers, delegatesTo, knowledgeBases, AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)));
    }
}
