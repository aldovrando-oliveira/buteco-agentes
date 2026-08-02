using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Queries.ListMcpServers;

public sealed class ListMcpServersQueryHandler(AppDbContext dbContext) : IQueryHandler<ListMcpServersQuery, IReadOnlyList<McpServerResponse>>
{
    public async ValueTask<IReadOnlyList<McpServerResponse>> Handle(ListMcpServersQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.McpServers
            .AsNoTracking()
            .OrderBy(mcpServer => mcpServer.CreatedAt)
            .Select(mcpServer => McpServerResponse.FromEntity(mcpServer))
            .ToListAsync(cancellationToken);
    }
}
