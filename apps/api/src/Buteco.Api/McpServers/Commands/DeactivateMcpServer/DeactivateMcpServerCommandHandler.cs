using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Commands.DeactivateMcpServer;

public sealed class DeactivateMcpServerCommandHandler(AppDbContext dbContext) : ICommandHandler<DeactivateMcpServerCommand, McpServerResponse?>
{
    public async ValueTask<McpServerResponse?> Handle(DeactivateMcpServerCommand command, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == command.Id, cancellationToken);

        if (mcpServer is null)
        {
            return null;
        }

        mcpServer.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return McpServerResponse.FromEntity(mcpServer);
    }
}
