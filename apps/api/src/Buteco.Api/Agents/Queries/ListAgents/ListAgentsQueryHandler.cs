using Buteco.Api.A2A;
using Buteco.Api.AgentKnowledgeBindings;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Agents.Queries.ListAgents;

public sealed class ListAgentsQueryHandler(AppDbContext dbContext,
    IOptions<PublicUrlOptions> publicUrlOptions) : IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>
{
    public async ValueTask<IReadOnlyList<AgentResponse>> Handle(ListAgentsQuery query, CancellationToken cancellationToken)
    {
        var agents = await dbContext.Agents
            .AsNoTracking()
            // Desempate estável: CreatedAt não é único — é atribuído no
            // construtor da entidade e dois registros podem compartilhar o
            // instante —, então ordenar só por ele deixa a ordem entre
            // empatados a cargo do plano do Postgres (api-response-ordering).
            .OrderBy(agent => agent.CreatedAt)
            .ThenBy(agent => agent.Id)
            .ToListAsync(cancellationToken);

        // Uma única consulta para todos os vínculos, em vez de N+1 por
        // agente (mesmo espírito de AgentMcpServerLookup, mas em lote).
        var bindings = await dbContext.AgentMcpServers
            .AsNoTracking()
            .Join(dbContext.McpServers, binding => binding.McpServerId, mcpServer => mcpServer.Id, (binding, mcpServer) => new { binding.AgentId, binding.AllowedTools, McpServer = mcpServer })
            .OrderBy(binding => binding.McpServer.Name)
            .ThenBy(binding => binding.McpServer.Id)
            .ToListAsync(cancellationToken);

        // A ORDEM VEM DA CONSULTA, DE PROPÓSITO — não reintroduza OrderBy em
        // memória aqui (design.md, D3). GET /agents/{id} ordena em SQL, com a
        // collation do Postgres; ordenar de novo aqui usaria o comparador de
        // string do .NET, que discorda dela para nomes que diferem em caixa e
        // pontuação (medido: "suporte-alfa" vs "Suporte Alfa" saem invertidos),
        // e ainda depende da cultura do processo. Duas superfícies com dois
        // comparadores devolvem ordens diferentes para o mesmo conjunto sem
        // nenhum empate envolvido. O GroupBy abaixo preserva a ordem de origem
        // dentro de cada grupo (documentado em Enumerable.GroupBy e medido em
        // 2.000 tentativas; item aberto em 02-HISTORICO_E_STATUS.md fixa o
        // gatilho no bump do runtime .NET).
        var mcpServersByAgentId = bindings
            .GroupBy(binding => binding.AgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<McpServerSummaryResponse>)group
                    .Select(binding => McpServerSummaryResponse.FromEntity(binding.McpServer, binding.AllowedTools))
                    .ToList());

        // Mesmo espírito do lote acima, agora para delegações de saída
        // (AgentDelegationLookup faz o equivalente por agente, mas em lote
        // aqui evita N+1 para a listagem inteira).
        var delegations = await dbContext.AgentDelegations
            .AsNoTracking()
            .Join(dbContext.Agents, delegation => delegation.TargetAgentId, agent => agent.Id, (delegation, agent) => new { delegation.SourceAgentId, TargetAgent = agent })
            .OrderBy(delegation => delegation.TargetAgent.Name)
            .ThenBy(delegation => delegation.TargetAgent.Id)
            .ToListAsync(cancellationToken);

        // Mesma razão de D3 registrada no bloco de mcpServers acima.
        var delegatesToBySourceAgentId = delegations
            .GroupBy(delegation => delegation.SourceAgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AgentSummaryResponse>)group
                    .Select(delegation => AgentSummaryResponse.FromEntity(delegation.TargetAgent))
                    .ToList());

        // Terceira consulta em lote, mesmo espírito das duas acima: sem ela,
        // chamar AgentKnowledgeBaseLookup por agente dentro do Select abaixo
        // seria N+1 na listagem inteira.
        var knowledgeBindings = await dbContext.AgentKnowledgeBases
            .AsNoTracking()
            .Join(dbContext.KnowledgeBases, binding => binding.KnowledgeBaseId, knowledgeBase => knowledgeBase.Id, (binding, knowledgeBase) => new { binding.AgentId, KnowledgeBase = knowledgeBase })
            .OrderBy(binding => binding.KnowledgeBase.Name)
            .ThenBy(binding => binding.KnowledgeBase.Id)
            .ToListAsync(cancellationToken);

        // Mesma razão de D3 registrada no bloco de mcpServers acima. Este
        // conjunto já tinha o desempate desde a etapa 3, mas ordenava em
        // memória — cumpria o requisito de agent-knowledge-binding com um
        // comparador diferente do da consulta por id.
        var knowledgeBasesByAgentId = knowledgeBindings
            .GroupBy(binding => binding.AgentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<KnowledgeBaseSummaryResponse>)group
                    .Select(binding => KnowledgeBaseSummaryResponse.FromEntity(binding.KnowledgeBase))
                    .ToList());

        return agents
            .Select(agent => AgentResponse.FromEntity(
                agent,
                mcpServersByAgentId.GetValueOrDefault(agent.Id, []),
                delegatesToBySourceAgentId.GetValueOrDefault(agent.Id, []),
                knowledgeBasesByAgentId.GetValueOrDefault(agent.Id, []),
                AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)))
            .ToList();
    }
}
