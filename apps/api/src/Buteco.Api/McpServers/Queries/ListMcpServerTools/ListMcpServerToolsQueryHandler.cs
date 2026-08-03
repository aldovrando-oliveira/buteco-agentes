using System.Security.Cryptography;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Security;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Queries.ListMcpServerTools;

public sealed class ListMcpServerToolsQueryHandler(
    AppDbContext dbContext,
    IMcpCredentialCipher credentialCipher,
    IMcpConnectionTester connectionTester) : IQueryHandler<ListMcpServerToolsQuery, ListMcpServerToolsResult>
{
    public async ValueTask<ListMcpServerToolsResult> Handle(ListMcpServerToolsQuery query, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .AsNoTracking()
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == query.Id, cancellationToken);

        if (mcpServer is null)
        {
            return ListMcpServerToolsResult.NotFound();
        }

        string? credential = null;
        if (mcpServer.AuthType != McpServerAuthType.None)
        {
            try
            {
                credential = credentialCipher.Decrypt(mcpServer.EncryptedCredential!);
            }
            catch (CryptographicException)
            {
                var decryptionFailure = McpServerToolsResponse.FromDiscoveryResult(McpToolDiscoveryResult.Failed(
                    McpConnectionTestFailureReason.CredentialDecryptionFailed,
                    "Não foi possível decifrar a credencial persistida com a chave de criptografia atualmente configurada."));

                return ListMcpServerToolsResult.Completed(decryptionFailure);
            }
        }

        var result = await connectionTester.ListToolsAsync(mcpServer.Url, mcpServer.AuthType, credential, cancellationToken);

        return ListMcpServerToolsResult.Completed(McpServerToolsResponse.FromDiscoveryResult(result));
    }
}
