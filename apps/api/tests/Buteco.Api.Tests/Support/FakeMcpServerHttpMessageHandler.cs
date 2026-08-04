using System.Net;
using System.Text;
using System.Text.Json;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Tool exposta pelo <see cref="FakeMcpServerHttpMessageHandler"/> em
/// respostas a `tools/list` (ver <see cref="FakeMcpServerHttpMessageHandler.AvailableTools"/>).
/// </summary>
public sealed record FakeMcpTool(string Name, string Description = "");

/// <summary>
/// Simula um servidor MCP remoto na camada HTTP, sem rede real — injetado no
/// lugar do <see cref="HttpMessageHandler"/> primário do cliente HTTP nomeado
/// "McpConnectionTester" (ver <see cref="Buteco.Api.McpServers.Connectivity.McpConnectionTester"/>).
/// Responde ao handshake `initialize` do protocolo MCP (Streamable HTTP)
/// com sucesso, opcionalmente exigindo um Bearer token específico, ou
/// simula um host inalcançável lançando <see cref="HttpRequestException"/>.
/// Responde também a `tools/list` com <see cref="AvailableTools"/> — usado
/// pelos testes de descoberta de tools e de validação de
/// `PUT /agents/{id}/mcp-servers` (change backend-mcp-selecao-tools).
/// </summary>
public sealed class FakeMcpServerHttpMessageHandler : HttpMessageHandler
{
    public bool SimulateUnreachable { get; set; }

    // Permite simular só um McpServer específico (por URL) inalcançável,
    // enquanto outros no mesmo teste respondem normalmente — necessário para
    // testar a rejeição atômica de PUT /agents/{id}/mcp-servers quando o
    // payload referencia múltiplos McpServers e só um deles falha o
    // handshake (Decision 2/6 do design.md da change backend-mcp-selecao-tools).
    public HashSet<string> UnreachableUrls { get; } = [];

    public string? RequiredBearerToken { get; set; }

    public IReadOnlyList<FakeMcpTool> AvailableTools { get; set; } = [];

    // Configurável para reproduzir servidores MCP reais que falam uma
    // revisão de protocolo diferente da mais recente (ex.: "2025-06-18") —
    // regressão do bug em que McpClientOptions.ProtocolVersion fixo fazia o
    // SDK rejeitar qualquer servidor que não respondesse com essa versão
    // exata (ver McpConnectionTester.BuildClientOptions).
    public string InitializeProtocolVersion { get; set; } = "2025-11-25";

    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;

        // Comparação via Uri (não string) para não depender de barra final
        // normalizada de forma diferente entre o valor configurado no teste
        // e o RequestUri de fato usado pelo transporte do SDK.
        if (SimulateUnreachable || UnreachableUrls.Any(url => new Uri(url) == request.RequestUri))
        {
            throw new HttpRequestException("Simulated unreachable host.");
        }

        if (RequiredBearerToken is not null)
        {
            var authHeader = request.Headers.Authorization;
            if (authHeader is null || authHeader.Scheme != "Bearer" || authHeader.Parameter != RequiredBearerToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var method = root.TryGetProperty("method", out var methodProperty) ? methodProperty.GetString() : null;

        if (method == "server/discover")
        {
            // Simula um servidor que ainda não implementa a revisão
            // 2026-07-28 do protocolo (a maioria dos servidores MCP reais,
            // hoje) — erro JSON-RPC "Method not found" padrão, que faz o SDK
            // cair para o handshake `initialize` clássico (McpClientOptions
            // .ProtocolVersion nulo, ver McpConnectionTester.BuildClientOptions).
            var id = root.GetProperty("id").GetRawText();
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }

        if (method == "initialize")
        {
            var id = root.GetProperty("id").GetRawText();
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"protocolVersion\":" + JsonSerializer.Serialize(InitializeProtocolVersion) + ",\"capabilities\":{},\"serverInfo\":{\"name\":\"fake-mcp-server\",\"version\":\"1.0.0\"}}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }

        if (method == "tools/list")
        {
            var id = root.GetProperty("id").GetRawText();
            var toolsJson = string.Join(",", AvailableTools.Select(tool =>
                $"{{\"name\":{JsonSerializer.Serialize(tool.Name)},\"description\":{JsonSerializer.Serialize(tool.Description)},\"inputSchema\":{{\"type\":\"object\"}}}}"));
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"tools\":[" + toolsJson + "]}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }

        // Notificações (ex.: notifications/initialized) não esperam corpo de resposta.
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
}
