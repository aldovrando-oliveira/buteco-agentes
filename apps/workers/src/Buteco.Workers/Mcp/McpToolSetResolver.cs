using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp.Entities;
using Buteco.Workers.Mcp.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Buteco.Workers.Mcp;

public sealed class McpToolSetResolver(
    IMcpCredentialCipher credentialCipher,
    McpTransportFactory transportFactory,
    ILogger<McpToolSetResolver> logger) : IMcpToolSetResolver
{
    // Separador de prefixo (design.md, Decision 3) — aplicado
    // incondicionalmente a toda tool resolvida, não só quando uma colisão é
    // detectada entre servidores diferentes vinculados ao mesmo agente.
    private const string ToolNameSeparator = "__";

    public async Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken)
    {
        var bindings = await (
            from binding in dbContext.AgentMcpServers.AsNoTracking()
            join server in dbContext.McpServers.AsNoTracking() on binding.McpServerId equals server.Id
            where binding.AgentId == agentId && server.IsActive
            select new { binding.AllowedTools, Server = server }
        ).ToListAsync(cancellationToken);

        var tools = new List<AITool>();
        var connections = new List<(McpClient Client, HttpClientTransport Transport)>();

        foreach (var binding in bindings)
        {
            var server = binding.Server;

            McpClient client;
            HttpClientTransport transport;
            try
            {
                // Só McpServer com AuthType != None tem EncryptedCredential
                // (invariante garantida por apps/api, ver McpServer.UpdateDetails) —
                // o operador nulo-tolerante documenta essa garantia.
                var credential = server.AuthType == McpServerAuthType.None
                    ? null
                    : credentialCipher.Decrypt(server.EncryptedCredential!);

                transport = transportFactory.BuildTransport(server.Url, server.AuthType, credential, $"mcp-tool-execution-{server.Id:N}");
                client = await McpClient.CreateAsync(transport, McpTransportFactory.BuildClientOptions(), cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Degradação por servidor (design.md, Decision 5): host
                // inalcançável, timeout, handshake falho, ou credencial que
                // não decifra com a chave atualmente configurada — o
                // servidor fica de fora do conjunto desta execução, a
                // resolução continua para os demais vinculados.
                logger.LogWarning(
                    exception,
                    "Não foi possível conectar ao McpServer {McpServerId} ({McpServerName}) — excluído do conjunto de tools desta execução.",
                    server.Id,
                    server.Name);
                continue;
            }

            // Registrado antes de tools/list para que o McpToolSet feche
            // esta conexão no dispose mesmo se a listagem abaixo falhar.
            connections.Add((client, transport));

            try
            {
                var discoveredTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                var allowedToolNames = new HashSet<string>(binding.AllowedTools);

                foreach (var tool in discoveredTools)
                {
                    // Interseção entre tools/list e AllowedTools — tool
                    // removida do servidor desde o vínculo (drift) ou
                    // AllowedTools vazio caem naturalmente daqui, sem
                    // tratamento especial (design.md, Decision 2).
                    if (allowedToolNames.Contains(tool.Name))
                    {
                        tools.Add(tool.WithName($"{server.Name}{ToolNameSeparator}{tool.Name}"));
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Não foi possível listar as tools do McpServer {McpServerId} ({McpServerName}) — excluído do conjunto de tools desta execução.",
                    server.Id,
                    server.Name);
            }
        }

        return new McpToolSet(tools, connections);
    }
}
