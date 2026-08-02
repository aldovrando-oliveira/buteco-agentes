using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Security;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Commands.UpdateMcpServer;

public sealed class UpdateMcpServerCommandHandler(AppDbContext dbContext, IMcpCredentialCipher credentialCipher)
    : ICommandHandler<UpdateMcpServerCommand, UpdateMcpServerResult>
{
    public async ValueTask<UpdateMcpServerResult> Handle(UpdateMcpServerCommand command, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == command.Id, cancellationToken);

        if (mcpServer is null)
        {
            return UpdateMcpServerResult.NotFound();
        }

        // Limpa a credencial anteriormente persistida quando AuthType
        // transiciona para None (Decision 6 do design.md).
        mcpServer.UpdateDetails(command.Name, command.Description, command.Url, command.AuthType);

        if (command.AuthType != McpServerAuthType.None && !string.IsNullOrWhiteSpace(command.Credential))
        {
            mcpServer.SetEncryptedCredential(credentialCipher.Encrypt(command.Credential));
        }

        if (mcpServer.AuthType != McpServerAuthType.None && mcpServer.EncryptedCredential is null)
        {
            return UpdateMcpServerResult.CredentialMissing();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateMcpServerResult.Success(McpServerResponse.FromEntity(mcpServer));
    }
}
