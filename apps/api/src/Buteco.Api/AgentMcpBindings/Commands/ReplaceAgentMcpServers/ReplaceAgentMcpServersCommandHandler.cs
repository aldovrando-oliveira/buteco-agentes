using Buteco.Api.A2A;
using Buteco.Api.AgentDelegations;
using Buteco.Api.AgentMcpBindings.Entities;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Security;
using Buteco.Api.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;

public sealed class ReplaceAgentMcpServersCommandHandler(
    AppDbContext dbContext,
    IMcpCredentialCipher credentialCipher,
    IMcpConnectionTester connectionTester,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<ReplaceAgentMcpServersCommand, ReplaceAgentMcpServersResult>
{
    public async ValueTask<ReplaceAgentMcpServersResult> Handle(ReplaceAgentMcpServersCommand command, CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .FirstOrDefaultAsync(agent => agent.Id == command.AgentId, cancellationToken);

        if (agent is null)
        {
            return ReplaceAgentMcpServersResult.AgentNotFound();
        }

        // Último a aparecer no payload vence, caso o mesmo McpServerId seja
        // referenciado mais de uma vez (mesma tolerância que o Distinct()
        // aplicado ao shape anterior, agora sobre um objeto composto).
        var requestedBindings = command.Bindings
            .GroupBy(binding => binding.McpServerId)
            .Select(group => group.Last())
            .ToList();

        var requestedIds = requestedBindings.Select(binding => binding.McpServerId).ToList();

        var mcpServers = await dbContext.McpServers
            .Where(mcpServer => requestedIds.Contains(mcpServer.Id))
            .ToListAsync(cancellationToken);

        var invalidIds = requestedIds.Except(mcpServers.Select(mcpServer => mcpServer.Id)).ToList();
        if (invalidIds.Count > 0)
        {
            return ReplaceAgentMcpServersResult.InvalidIds(invalidIds);
        }

        var mcpServersById = mcpServers.ToDictionary(mcpServer => mcpServer.Id);

        // Validação ao vivo (Decision 2 do design.md): só conecta ao
        // McpServer quando há alguma tool para validar — allowedTools vazio
        // não exige handshake nenhum (Decision 4: vínculo com zero tools é
        // válido por si só, sem nada a checar contra o servidor).
        foreach (var binding in requestedBindings)
        {
            if (binding.AllowedTools.Count == 0)
            {
                continue;
            }

            var mcpServer = mcpServersById[binding.McpServerId];
            var discovery = await DiscoverToolsAsync(mcpServer, cancellationToken);

            if (discovery.Failure is not null)
            {
                return ReplaceAgentMcpServersResult.HandshakeFailed(discovery.Failure);
            }

            var rejectedTools = binding.AllowedTools.Where(tool => !discovery.AvailableToolNames!.Contains(tool)).ToList();
            if (rejectedTools.Count > 0)
            {
                return ReplaceAgentMcpServersResult.InvalidToolsFound([new InvalidMcpServerTool(mcpServer.Id, rejectedTools)]);
            }
        }

        var currentBindings = await dbContext.AgentMcpServers
            .Where(binding => binding.AgentId == command.AgentId)
            .ToListAsync(cancellationToken);
        dbContext.AgentMcpServers.RemoveRange(currentBindings);

        foreach (var binding in requestedBindings)
        {
            dbContext.AgentMcpServers.Add(new AgentMcpServer(command.AgentId, binding.McpServerId, binding.AllowedTools));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var linkedMcpServers = await AgentMcpServerLookup.GetLinkedMcpServersAsync(dbContext, agent.Id, cancellationToken);
        var delegatesTo = await AgentDelegationLookup.GetDelegateTargetsAsync(dbContext, agent.Id, cancellationToken);

        return ReplaceAgentMcpServersResult.Success(AgentResponse.FromEntity(agent, linkedMcpServers, delegatesTo, AgentA2AAddressBuilder.Build(publicUrlOptions.Value, agent.Id)));
    }

    private async Task<(McpServerHandshakeFailure? Failure, IReadOnlyList<string>? AvailableToolNames)> DiscoverToolsAsync(McpServer mcpServer, CancellationToken cancellationToken)
    {
        string? credential = null;
        if (mcpServer.AuthType != McpServerAuthType.None)
        {
            try
            {
                credential = credentialCipher.Decrypt(mcpServer.EncryptedCredential!);
            }
            catch (CryptographicException)
            {
                var failure = new McpServerHandshakeFailure(
                    mcpServer.Id,
                    McpConnectionTestFailureReason.CredentialDecryptionFailed,
                    "Não foi possível decifrar a credencial persistida com a chave de criptografia atualmente configurada.");

                return (failure, null);
            }
        }

        var discovery = await connectionTester.ListToolsAsync(mcpServer.Url, mcpServer.AuthType, credential, cancellationToken);

        return discovery.Success
            ? (null, discovery.Tools!.Select(tool => tool.Name).ToList())
            : (new McpServerHandshakeFailure(mcpServer.Id, discovery.FailureReason!.Value, discovery.Message!), null);
    }
}
