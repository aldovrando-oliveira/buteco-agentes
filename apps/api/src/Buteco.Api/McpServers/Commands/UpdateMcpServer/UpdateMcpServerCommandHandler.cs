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

        // Limpa a credencial persistida quando o AuthType muda ou vai para None
        // (D4 da change mcp-autenticacao-header-customizado, que estende a
        // Decision 6 de backend-mcp-catalogo-vinculo — ver McpServer.UpdateDetails).
        mcpServer.UpdateDetails(command.Name, command.Description, command.Url, command.AuthType, command.AuthHeaderName);

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
