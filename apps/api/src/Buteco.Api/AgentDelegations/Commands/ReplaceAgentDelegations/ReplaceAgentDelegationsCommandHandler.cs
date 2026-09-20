using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations.Entities;
using Buteco.Api.AgentKnowledgeBindings;
using Buteco.Api.AgentMcpBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;

public sealed class ReplaceAgentDelegationsCommandHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<ReplaceAgentDelegationsCommand, ReplaceAgentDelegationsResult>
{
    public async ValueTask<ReplaceAgentDelegationsResult> Handle(ReplaceAgentDelegationsCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.AgentId, cancellationToken);

        if (agent is null)
        {
            return ReplaceAgentDelegationsResult.AgentNotFound();
        }

        var requestedTargetIds = command.TargetAgentIds.Distinct().ToList();

        // Auto-delegação (Decision 2 do design.md): ciclo de profundidade 1.
        // CONTINUA COM CHECAGEM PRÓPRIA embora a regra geral de ciclo abaixo já
        // a contenha — a mensagem "um agente não pode delegar para si mesmo" é
        // melhor que o caminho "A → A" formatado pela regra geral, e o cenário
        // de spec que a prende continua valendo palavra por palavra.
        if (requestedTargetIds.Contains(command.AgentId))
        {
            return ReplaceAgentDelegationsResult.SelfDelegation();
        }

        var existingTargetIds = await dbContext.Agents
            .Where(targetAgent => requestedTargetIds.Contains(targetAgent.Id))
            .Select(targetAgent => targetAgent.Id)
            .ToListAsync(cancellationToken);

        var invalidTargetIds = requestedTargetIds.Except(existingTargetIds).ToList();
        if (invalidTargetIds.Count > 0)
        {
            return ReplaceAgentDelegationsResult.InvalidIds(invalidTargetIds);
        }

        // CICLO DE DELEGAÇÃO DE QUALQUER COMPRIMENTO — a Decision 3 do design.md
        // de backend-agente-delegacao-catalogo-vinculo registrava controle de
        // ciclo geral como "fora de escopo desta camada". ELA ESTÁ CONTRARIADA
        // AQUI, com a causa real (convenção 9): o que era fora de escopo passou
        // a ter mecanismo de dano medido.
        //
        // O mecanismo: um ciclo A→B→…→A autotrava no advisory lock de contexto
        // de apps/workers. A task de A em profundidade 0 segura
        // pg_advisory_lock(hashtext(A), hashtext(contexto)) enquanto espera B, e
        // a task de A em profundidade 2 pede o mesmo lock e bloqueia. Medido na
        // exploração replicas-de-worker com QUATRO instâncias de worker: não
        // adianta ter réplica, porque o recurso disputado é o lock e não o
        // consumidor. O DelegationDepthLimit (5) não cobre — a checagem de
        // profundidade roda antes da aquisição do lock, e o ciclo de dois saltos
        // trava em profundidade 2.
        //
        // E A CAMADA É ESTA porque este handler é o ESCRITOR ÚNICO de
        // agent_delegations: apps/workers só lê (AsNoTracking, entidade com
        // construtor e setters privados) e apps/inbox não toca a tabela. Fechado
        // o cadastro, o ciclo deixa de ter porta de entrada, e é por isso que
        // não há defesa em profundidade no runtime (design.md, D3). O gatilho de
        // reabertura é observável: o primeiro SEGUNDO escritor desta tabela.
        //
        // Uma consulta só, e a travessia é função pura sobre as arestas.
        var persistedEdges = await dbContext.AgentDelegations
            .AsNoTracking()
            .Select(delegation => new { delegation.SourceAgentId, delegation.TargetAgentId })
            .ToListAsync(cancellationToken);

        var graph = AgentDelegationCycleDetector.BuildGraph(
            persistedEdges.Select(edge => (edge.SourceAgentId, edge.TargetAgentId)),
            command.AgentId,
            requestedTargetIds);

        var cyclePath = AgentDelegationCycleDetector.FindCycleFrom(command.AgentId, graph);
        if (cyclePath is not null)
        {
            var namesById = await dbContext.Agents
                .AsNoTracking()
                .Where(pathAgent => cyclePath.Contains(pathAgent.Id))
                .ToDictionaryAsync(pathAgent => pathAgent.Id, pathAgent => pathAgent.Name, cancellationToken);

            return ReplaceAgentDelegationsResult.Cycle(
                [.. cyclePath.Select(id => namesById.TryGetValue(id, out var name) ? name : id.ToString())]);
        }

        var currentDelegations = await dbContext.AgentDelegations
            .Where(delegation => delegation.SourceAgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentDelegations.RemoveRange(currentDelegations);

        foreach (var targetAgentId in requestedTargetIds)
        {
            dbContext.AgentDelegations.Add(new AgentDelegation(command.AgentId, targetAgentId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);
        var knowledgeBases = await AgentKnowledgeBaseLookup.GetLinkedKnowledgeBasesAsync(dbContext, agent.Id, cancellationToken);

        return ReplaceAgentDelegationsResult.Success(AgentResponse.FromEntity(agent, mcpServers, delegatesTo, knowledgeBases, AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)));
    }
}
