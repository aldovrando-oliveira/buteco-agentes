using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Security;
using Mediator;

namespace Buteco.Api.McpServers.Commands.CreateMcpServer;

public sealed class CreateMcpServerCommandHandler(AppDbContext dbContext, IMcpCredentialCipher credentialCipher)
    : ICommandHandler<CreateMcpServerCommand, McpServerResponse>
{
    public async ValueTask<McpServerResponse> Handle(CreateMcpServerCommand command, CancellationToken cancellationToken)
    {
        var encryptedCredential = command.AuthType == McpServerAuthType.None
            ? null
            : credentialCipher.Encrypt(command.Credential!);

        var mcpServer = new McpServer(command.Name, command.Description, command.Url, command.AuthType, encryptedCredential);

        dbContext.McpServers.Add(mcpServer);
        await dbContext.SaveChangesAsync(cancellationToken);

        return McpServerResponse.FromEntity(mcpServer);
    }
}
