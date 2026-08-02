using System.Security.Cryptography;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Security;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Commands.TestSavedMcpServerConnection;

public sealed class TestSavedMcpServerConnectionCommandHandler(
    AppDbContext dbContext,
    IMcpCredentialCipher credentialCipher,
    IMcpConnectionTester connectionTester) : ICommandHandler<TestSavedMcpServerConnectionCommand, TestSavedMcpServerConnectionResult>
{
    public async ValueTask<TestSavedMcpServerConnectionResult> Handle(TestSavedMcpServerConnectionCommand command, CancellationToken cancellationToken)
    {
        var mcpServer = await dbContext.McpServers
            .AsNoTracking()
            .FirstOrDefaultAsync(mcpServer => mcpServer.Id == command.Id, cancellationToken);

        if (mcpServer is null)
        {
            return TestSavedMcpServerConnectionResult.NotFound();
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
                var decryptionFailure = McpConnectionTestResult.Failed(
                    McpConnectionTestFailureReason.CredentialDecryptionFailed,
                    "Não foi possível decifrar a credencial persistida com a chave de criptografia atualmente configurada.");

                return TestSavedMcpServerConnectionResult.Completed(decryptionFailure);
            }
        }

        var result = await connectionTester.TestAsync(mcpServer.Url, mcpServer.AuthType, credential, cancellationToken);

        return TestSavedMcpServerConnectionResult.Completed(result);
    }
}
