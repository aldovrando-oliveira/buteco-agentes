using System.Net;
using System.Text;
using System.Text.Json;

namespace Buteco.Workers.Tests.Mcp.Support;

/// <summary>
/// Tool exposta por um servidor fake em respostas a `tools/list`.
/// </summary>
public sealed record FakeMcpTool(string Name, string Description = "");

/// <summary>
/// Configuração de um servidor MCP fake individual, endereçado por URL (ver
/// <see cref="FakeMcpServerHttpMessageHandler.ConfigureServer"/>) — permite
/// simular múltiplos <c>McpServer</c>s distintos (com comportamentos
/// independentes: um inalcançável enquanto outro responde, tools diferentes
/// por servidor, etc.) atrás do mesmo <see cref="HttpClient"/> nomeado
/// compartilhado que <c>apps/workers</c> usa para toda conexão MCP.
/// </summary>
public sealed class FakeMcpServerConfig
{
    public bool Unreachable { get; set; }

    /// <summary>
    /// Simula o servidor ficando indisponível especificamente durante
    /// `tools/call`, depois de já ter respondido `initialize`/`tools/list`
    /// com sucesso — cenário de uma tool call falhando em andamento (task
    /// 4.3), distinto de <see cref="Unreachable"/> (que falha já na
    /// resolução, antes de RunAsync começar).
    /// </summary>
    public bool FailToolCalls { get; set; }

    public string? RequiredBearerToken { get; set; }

    public IReadOnlyList<FakeMcpTool> AvailableTools { get; set; } = [];

    // Configurável para reproduzir servidores MCP reais que falam uma
    // revisão de protocolo diferente da mais recente (ex.: "2025-06-18") —
    // regressão do bug em que McpClientOptions.ProtocolVersion fixo fazia o
    // SDK rejeitar qualquer servidor que não respondesse com essa versão
    // exata (ver McpTransportFactory.BuildClientOptions).
    public string InitializeProtocolVersion { get; set; } = "2025-11-25";

    /// <summary>
    /// Computa o texto de resultado de `tools/call` a partir do nome da tool
    /// chamada. Default devolve um texto previsível o suficiente para os
    /// testes de round-trip afirmarem que o resultado real da tool chegou
    /// até a resposta final da task.
    /// </summary>
    public Func<string, string> ToolCallResult { get; set; } = toolName => $"resultado de {toolName}";
}

/// <summary>
/// Simula um ou mais servidores MCP remotos na camada HTTP, sem rede real —
/// injetado no lugar do <see cref="HttpMessageHandler"/> primário do
/// <see cref="HttpClient"/> nomeado usado por
/// <see cref="Buteco.Workers.Mcp.McpTransportFactory"/>. Fixture próprio de
/// <c>apps/workers</c> (não reaproveita o de <c>apps/api</c> — isolamento
/// entre apps), mas segue o mesmo formato de payload JSON-RPC já validado lá,
/// estendido com suporte a `tools/call` (que <c>apps/api</c> nunca precisa
/// simular, porque só descobre tools, nunca as invoca de verdade).
/// </summary>
public sealed class FakeMcpServerHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<Uri, FakeMcpServerConfig> _servers = new();
    private readonly Dictionary<(Uri Url, string Method), int> _requestCounts = new();

    /// <summary>
    /// Total de requisições HTTP recebidas, de qualquer servidor configurado
    /// ou não — usado para provar "nenhuma tentativa de conexão" (ex.:
    /// agente sem McpServer vinculado, McpServer inativo).
    /// </summary>
    public int TotalRequestsReceived { get; private set; }

    public void ConfigureServer(string url, FakeMcpServerConfig config) => _servers[new Uri(url)] = config;

    public int CountRequests(string url, string method) =>
        _requestCounts.GetValueOrDefault((new Uri(url), method));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TotalRequestsReceived++;

        var uri = request.RequestUri ?? throw new InvalidOperationException("Request sem RequestUri.");

        if (!_servers.TryGetValue(uri, out var config))
        {
            throw new InvalidOperationException($"Nenhum FakeMcpServerConfig configurado para '{uri}'. Chame ConfigureServer antes do teste conectar.");
        }

        if (config.Unreachable)
        {
            throw new HttpRequestException("Simulated unreachable host.");
        }

        if (config.RequiredBearerToken is not null)
        {
            var authHeader = request.Headers.Authorization;
            if (authHeader is null || authHeader.Scheme != "Bearer" || authHeader.Parameter != config.RequiredBearerToken)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
        }

        if (request.Method == HttpMethod.Delete)
        {
            // Encerramento de sessão (OwnsSession, default true no transporte
            // real de apps/workers, ver McpTransportFactory) — sem corpo
            // JSON-RPC, só confirma o encerramento.
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            // Stream GET não solicitado ou notificação sem corpo — sem
            // método JSON-RPC para rotear.
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var method = root.TryGetProperty("method", out var methodProperty) ? methodProperty.GetString() : null;

        if (method is null || !root.TryGetProperty("id", out var idProperty))
        {
            // Notificações JSON-RPC (ex.: notifications/initialized, enviada
            // pelo SDK logo após o handshake initialize) têm "method" mas
            // nunca "id" — por definição não esperam resposta correlacionada.
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        _requestCounts[(uri, method)] = _requestCounts.GetValueOrDefault((uri, method)) + 1;

        var id = idProperty.GetRawText();

        if (method == "server/discover")
        {
            // Simula um servidor que ainda não implementa a revisão
            // 2026-07-28 do protocolo (a maioria dos servidores MCP reais,
            // hoje) — erro JSON-RPC "Method not found" padrão, que faz o SDK
            // cair para o handshake `initialize` clássico (McpClientOptions
            // .ProtocolVersion nulo, ver McpTransportFactory.BuildClientOptions).
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}";
            return JsonResponse(responseJson);
        }

        if (method == "initialize")
        {
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"protocolVersion\":" + JsonSerializer.Serialize(config.InitializeProtocolVersion) + ",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"fake-mcp-server\",\"version\":\"1.0.0\"}}}";
            return JsonResponse(responseJson);
        }

        if (method == "tools/list")
        {
            var toolsJson = string.Join(",", config.AvailableTools.Select(tool =>
                $"{{\"name\":{JsonSerializer.Serialize(tool.Name)},\"description\":{JsonSerializer.Serialize(tool.Description)},\"inputSchema\":{{\"type\":\"object\"}}}}"));
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"tools\":[" + toolsJson + "]}}";
            return JsonResponse(responseJson);
        }

        if (method == "tools/call")
        {
            if (config.FailToolCalls)
            {
                throw new HttpRequestException("Simulated failure during tools/call.");
            }

            var toolName = root.GetProperty("params").GetProperty("name").GetString()!;
            var resultText = config.ToolCallResult(toolName);
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(resultText) + "}],\"isError\":false}}";
            return JsonResponse(responseJson);
        }

        // Outros métodos (ex.: notifications/initialized com id, session
        // teardown) — resposta vazia genérica, suficiente para não travar o
        // SDK cliente.
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
