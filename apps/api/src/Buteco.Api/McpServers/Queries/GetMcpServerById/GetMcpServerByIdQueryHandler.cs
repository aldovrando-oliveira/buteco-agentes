using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Queries.GetMcpServerById;

public sealed class GetMcpServerByIdQueryHandler(AppDbContext dbContext) : IQueryHandler<GetMcpServerByIdQuery, McpServerResponse?>
{
    public async ValueTask<McpServerResponse?> Handle(GetMcpServerByIdQuery query, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .AsNoTracking()
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == query.Id, cancellationToken);

        return mcpServer is null ? null : McpServerResponse.FromEntity(mcpServer);
    }
}
