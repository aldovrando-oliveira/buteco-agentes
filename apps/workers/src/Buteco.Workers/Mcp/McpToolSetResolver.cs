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

    // Bug em produção: McpServer.Name é texto livre (sem validação de
    // formato em apps/api) e alimentava sem sanitização o nome exposto ao
    // LLM via WithName — um nome como "Zendesk MCP" (com espaço) violava a
    // regra de nome de function declaration do Gemini (que o documento de
    // descoberta oficial declara como "a-z, A-Z, 0-9, or contain underscores,
    // colons, dots, and dashes, with a maximum length of 128" — a âncora de
    // caractere inicial que este comentário afirmava antes NÃO está na fonte
    // primária, ver V4 do design.md da change dedupe-global-nome-de-tool) e a
    // chamada falhava 100% das vezes para qualquer McpServer com caractere fora
    // desse conjunto no nome. O resolver não sabe qual provedor o agente usa
    // (Decision 7 do design.md da change backend-multi-provedor-llm resolve o
    // IChatClient só depois, em ChatClientResolver), então o nome sanitizado
    // precisa satisfazer o limite mais apertado entre os provedores suportados —
    // 64 caracteres e [a-zA-Z0-9_-], verificado em fonte primária apenas para
    // OpenAI (Chat Completions) e Gemini; o Anthropic não publica o seu (ver o
    // comentário de ToolNameSanitizer.MaxToolNameLength e R8). Sanitização em
    // ToolNameSanitizer, reaproveitada também pela resolução de tools de
    // delegação (ver design.md da change apps-workers-delegacao-execucao,
    // Decision 9).

    public async Task<McpToolSet> ResolveAsync(AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken)
    {
        // orderby explícito (design.md da change dedupe-global-nome-de-tool,
        // Decisão 8): sem ele a ordem era a que o Postgres devolvesse, e com
        // ela mudava qual tool mantém o nome-base num desempate de dedupe — o
        // conjunto de nomes deixava de ser estável entre execuções. Por
        // McpServerId, e não por Name, porque o nome é editável: renomear um
        // servidor não deve reordenar o conjunto.
        var bindings = await (
            from binding in dbContext.AgentMcpServers.AsNoTracking()
            join server in dbContext.McpServers.AsNoTracking() on binding.McpServerId equals server.Id
            where binding.AgentId == agentId && server.IsActive
            orderby binding.McpServerId
            select new { binding.AllowedTools, Server = server }
        ).ToListAsync(cancellationToken);

        var tools = new List<AITool>();
        var connections = new List<(McpClient Client, HttpClientTransport Transport)>();

        foreach (var binding in bindings)
        {
            var server = binding.Server;

            // Tipo de autenticação que esta versão não conhece (D5 da change
            // mcp-autenticacao-header-customizado): o conversor de AppDbContext o
            // materializa como null em vez de lançar. Sai do conjunto SEM decifrar a
            // credencial e SEM conectar — não há como montar a autenticação.
            if (server.AuthType is not { } authType)
            {
                logger.LogWarning(
                    "O McpServer {McpServerId} ({McpServerName}) tem um tipo de autenticação que esta versão de apps/workers não conhece — excluído do conjunto de tools desta execução.",
                    server.Id,
                    server.Name);
                continue;
            }

            // Nome de header que o .NET recusa (D3): o SDK lançaria
            // InvalidOperationException com o VALOR do header — a credencial
            // decifrada — na mensagem (CopyAdditionalHeaders, decompilado, V2), e o
            // LogWarning(exception, ...) do catch abaixo a gravaria no log. apps/api
            // valida o nome no cadastro; esta reconferência cobre a linha que não
            // passou por lá (SQL direto, validação regredida). Só a regra que fecha
            // o vazamento é reconferida: a lista de reservados vive em apps/api. E
            // a exceção NÃO é passada ao logger.
            if (authType == McpServerAuthType.CustomHeader && !IsAcceptedAsRequestHeader(server.AuthHeaderName))
            {
                logger.LogWarning(
                    "O McpServer {McpServerId} ({McpServerName}) tem um nome de header ('{AuthHeaderName}') que não pode ser usado em uma requisição — excluído do conjunto de tools desta execução.",
                    server.Id,
                    server.Name,
                    server.AuthHeaderName);
                continue;
            }

            McpClient client;
            HttpClientTransport transport;
            try
            {
                // Só McpServer com AuthType != None tem EncryptedCredential
                // (invariante garantida por apps/api, ver McpServer.UpdateDetails) —
                // o operador nulo-tolerante documenta essa garantia.
                var credential = authType == McpServerAuthType.None
                    ? null
                    : credentialCipher.Decrypt(server.EncryptedCredential!);

                transport = transportFactory.BuildTransport(server.Url, authType, credential, server.AuthHeaderName, $"mcp-tool-execution-{server.Id:N}");
                client = await McpClient.CreateAsync(transport, McpTransportFactory.BuildClientOptions(), cancellationToken: cancellationToken);
            }
            catch (Exception exception) when (!IsShutdownCancellation(exception, cancellationToken))
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
                        tools.Add(tool.WithName(BuildSafeToolName(server.Name, tool.Name)));
                    }
                }
            }
            catch (Exception exception) when (!IsShutdownCancellation(exception, cancellationToken))
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

    // CANCELAMENTO NÃO É SÓ PARADA. O ConnectTimeout do SocketsHttpHandler chega
    // como TaskCanceledException (⊃ TimeoutException "A connection could not be
    // established within the configured ConnectTimeout"), com o token da execução
    // INTACTO. Um filtro `exception is not OperationCanceledException` deixava esse
    // timeout escapar da degradação por servidor: o servidor inalcançável derrubava
    // a TASK em vez de sair do conjunto. Medido no guarda
    // ResolveAsync_ServerWhoseConnectionNeverCompletes_IsExcludedWithinTheConnectTimeout,
    // que reprovou por exceção com o timeout aplicado e este filtro ausente
    // (change timeout-de-conexao-saida-workers, D4).
    //
    // Só é parada quando o token DESTA execução foi cancelado — a mesma regra de
    // AgentExecutionService.IsShutdownCancellation.
    // A MESMA chamada que o SDK faz com cada header adicional
    // (TryAddWithoutValidation), com valor fictício: quando ela devolve false, o
    // SDK lança com o valor real na mensagem. Mesmo critério de
    // McpAuthHeaderRules.IsAcceptedAsRequestHeader em apps/api — replicado, não
    // compartilhado (isolamento entre apps; são três linhas).
    private static bool IsAcceptedAsRequestHeader(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        using var probe = new HttpRequestMessage();
        return probe.Headers.TryAddWithoutValidation(name, "x");
    }

    private static bool IsShutdownCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static string BuildSafeToolName(string serverName, string toolName) =>
        ToolNameSanitizer.Sanitize($"{serverName}{ToolNameSeparator}{toolName}");
}
