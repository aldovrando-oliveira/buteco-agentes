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

    // "2025-11-25" é a última revisão do protocolo MCP que ainda usa o
    // handshake `initialize` clássico (a revisão seguinte, 2026-07-28,
    // substituiu isso por um mecanismo de metadata por-requisição). Fixar
    // essa versão força o SDK a sempre fazer o handshake `initialize` de
    // forma determinística — documentado pelo próprio SDK como o efeito de
    // configurar McpClientOptions.ProtocolVersion para uma versão que ainda
    // suporta sessões Streamable HTTP — em vez de primeiro sondar a versão
    // mais nova (`server/discover`) e só cair para `initialize` se o
    // servidor não suportar. Adequado aqui porque este teste é uma
    // verificação pontual de conectividade/credencial, não uma sessão
    // MCP de longa duração que precisaria da revisão mais recente.
    private const string InitializeHandshakeProtocolVersion = "2025-11-25";

    public async Task<McpConnectionTestResult> TestAsync(string url, McpServerAuthType authType, string? credential, CancellationToken cancellationToken)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(url),
            Name = "mcp-connection-test",
            // Teste pontual: não precisa do stream GET não solicitado nem de
            // encerrar sessão no dispose (evita uma requisição DELETE extra
            // que não importa para o resultado do teste).
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
        await using var transport = new HttpClientTransport(transportOptions, httpClient, ownsHttpClient: false);

        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = InitializeHandshakeProtocolVersion,
        };

        try
        {
            await using var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
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
}
