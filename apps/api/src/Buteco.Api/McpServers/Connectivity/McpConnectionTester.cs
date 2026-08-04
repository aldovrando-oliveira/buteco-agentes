using System.Net;
using Buteco.Api.McpServers.Entities;
using ModelContextProtocol.Client;

namespace Buteco.Api.McpServers.Connectivity;

/// <summary>
/// Usa o SDK oficial (<c>ModelContextProtocol.Core</c>) para conectar de
/// verdade a um servidor MCP remoto e validar a configuração via o
/// handshake <c>initialize</c>. O <see cref="HttpClient"/> usado vem de
/// <see cref="IHttpClientFactory"/> (cliente nomeado "McpConnectionTester")
/// especificamente para que testes possam substituir o
/// <see cref="HttpMessageHandler"/> primário sem rede real, mesmo espírito
/// do spy de <c>ITaskJobPublisher</c> já usado no projeto.
/// </summary>
public sealed class McpConnectionTester(IHttpClientFactory httpClientFactory) : IMcpConnectionTester
{
    public const string HttpClientName = "McpConnectionTester";

    public async Task<McpConnectionTestResult> TestAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken)
    {
        await using var transport = BuildTransport(url, authType, credential);

        try
        {
            await using var client = await McpClient.CreateAsync(transport, BuildClientOptions(), cancellationToken: cancellationToken);
            return McpConnectionTestResult.Successful();
        }
        catch (HttpRequestException exception)
        {
            return exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpConnectionTestResult.Failed(McpConnectionTestFailureReason.CredentialRejected, "O servidor MCP rejeitou a credencial informada.")
                : McpConnectionTestResult.Failed(McpConnectionTestFailureReason.HostUnreachable, $"Não foi possível conectar ao servidor MCP: {exception.Message}");
        }
        catch (Exception exception)
        {
            return McpConnectionTestResult.Failed(McpConnectionTestFailureReason.Unknown, $"Falha inesperada ao testar o servidor MCP: {exception.Message}");
        }
    }

    public async Task<McpToolDiscoveryResult> ListToolsAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken)
    {
        await using var transport = BuildTransport(url, authType, credential);

        try
        {
            await using var client = await McpClient.CreateAsync(transport, BuildClientOptions(), cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

            return McpToolDiscoveryResult.Successful(tools
                .Select(tool => new McpToolDescriptor(tool.Name, tool.Description ?? string.Empty))
                .ToList());
        }
        catch (HttpRequestException exception)
        {
            return exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.CredentialRejected, "O servidor MCP rejeitou a credencial informada.")
                : McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.HostUnreachable, $"Não foi possível conectar ao servidor MCP: {exception.Message}");
        }
        catch (Exception exception)
        {
            return McpToolDiscoveryResult.Failed(McpConnectionTestFailureReason.Unknown, $"Falha inesperada ao listar as tools do servidor MCP: {exception.Message}");
        }
    }

    // Construção do transporte compartilhada entre TestAsync e ListToolsAsync
    // (Decision 3 do design.md da change backend-mcp-selecao-tools) — evita
    // duas implementações paralelas da mesma lógica de conexão MCP.
    private HttpClientTransport BuildTransport(string url, McpServerAuthType authType, string? credential)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(url),
            Name = "mcp-connection-test",
            // Teste pontual: não precisa do stream GET não solicitado nem de
            // encerrar sessão no dispose (evita uma requisição DELETE extra
            // que não importa para o resultado do teste/descoberta).
            EnableStandaloneGetStream = false,
            OwnsSession = false,
        };

        if (authType == McpServerAuthType.BearerToken)
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {credential}",
            };
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        return new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: false);
    }

    // Bug em produção: fixar McpClientOptions.ProtocolVersion (ex.:
    // "2025-11-25") faz o SDK pedir exatamente essa versão e recusar
    // downgrade — documentado pelo próprio SDK: "the client requests
    // exactly this version and refuses to downgrade below it, throwing an
    // McpException instead of falling back". Servidores MCP reais e
    // amplamente usados ainda falam revisões mais antigas (ex.:
    // "2025-06-18"), então fixar qualquer versão única rejeita, na prática,
    // qualquer servidor de terceiros que não fale exatamente essa versão —
    // inviável para testar/conectar a um McpServer arbitrário cuja versão
    // de protocolo não se controla. Deixar ProtocolVersion nulo (default)
    // habilita o fallback automático do próprio SDK: sonda com
    // `server/discover` (revisão mais nova) e cai para o handshake
    // `initialize` clássico, negociando a versão que o servidor de fato
    // oferece, em vez de falhar.
    private static McpClientOptions BuildClientOptions() => new();
}
