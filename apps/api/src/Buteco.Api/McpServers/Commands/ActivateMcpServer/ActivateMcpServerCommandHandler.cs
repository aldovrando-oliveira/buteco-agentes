using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Commands.ActivateMcpServer;

public sealed class ActivateMcpServerCommandHandler(AppDbContext dbContext) : ICommandHandler<ActivateMcpServerCommand, McpServerResponse?>
{
    public async ValueTask<McpServerResponse?> Handle(ActivateMcpServerCommand command, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == command.Id, cancellationToken);

        if (mcpServer is null)
        {
            return null;
        }

        mcpServer.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return McpServerResponse.FromEntity(mcpServer);
    }
}
