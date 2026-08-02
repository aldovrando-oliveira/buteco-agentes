using Buteco.Api.AgentMcpBindings.Entities;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

public sealed class ReplaceAgentMcpServersCommandHandler(AppDbContext dbContext)
    : ICommandHandler<ReplaceAgentMcpServersCommand, ReplaceAgentMcpServersResult>
{
    public async ValueTask<ReplaceAgentMcpServersResult> Handle(ReplaceAgentMcpServersCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.AgentId, cancellationToken);

        if (agent is null)
        {
            return ReplaceAgentMcpServersResult.AgentNotFound();
        }

        var requestedIds = command.McpServerIds.Distinct().ToList();

        var existingMcpServerIds = await dbContext.McpServers
            .Where(mcpServer => requestedIds.Contains(mcpServer.Id))
            .Select(mcpServer => mcpServer.Id)
            .ToListAsync(cancellationToken);

        var invalidIds = requestedIds.Except(existingMcpServerIds).ToList();
        if (invalidIds.Count > 0)
        {
            return ReplaceAgentMcpServersResult.InvalidIds(invalidIds);
        }

        var currentBindings = await dbContext.AgentMcpServers
            .Where(binding => binding.AgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentMcpServers.RemoveRange(currentBindings);

        foreach (var mcpServerId in requestedIds)
        {
            dbContext.AgentMcpServers.Add(new AgentMcpServer(command.AgentId, mcpServerId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var mcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);

        return ReplaceAgentMcpServersResult.Success(AgentResponse.FromEntity(agent, mcpServers));
    }
}
