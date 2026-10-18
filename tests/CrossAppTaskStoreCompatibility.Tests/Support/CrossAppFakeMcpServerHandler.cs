using System.Net;
using System.Text;
using System.Text.Json;

namespace CrossAppTaskStoreCompatibility.Tests.Support;

/// <summary>
/// Servidor MCP mínimo na camada HTTP, próprio deste projeto: os fakes de
/// <c>apps/api</c> e <c>apps/workers</c> moram nos projetos de teste deles e não
/// são referenciados daqui. Responde à sonda <c>server/discover</c> com "Method
/// not found" (o SDK cai para <c>initialize</c>), a <c>initialize</c> e a
/// <c>tools/list</c>, e registra os headers de cada requisição — é o que o
/// teste de acordo lê para afirmar que o header chegou com a credencial
/// original.
/// </summary>
public sealed class CrossAppFakeMcpServerHandler(IReadOnlyList<string> toolNames) : HttpMessageHandler
{
    private readonly List<IReadOnlyDictionary<string, string>> _receivedHeaders = [];

    public IReadOnlyList<IReadOnlyDictionary<string, string>> ReceivedHeaders
    {
        get
        {
            lock (_receivedHeaders)
            {
                return _receivedHeaders.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_receivedHeaders)
        {
            _receivedHeaders.Add(request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase));
        }

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new HttpResponseMessage(request.Method == HttpMethod.Delete ? HttpStatusCode.OK : HttpStatusCode.Accepted);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("id", out var idProperty) || !root.TryGetProperty("method", out var methodProperty))
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        var id = idProperty.GetRawText();
        var responseJson = methodProperty.GetString() switch
        {
            "server/discover" => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}",
            "initialize" => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"cross-app-fake\",\"version\":\"1.0.0\"}}}",
            "tools/list" => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{\"tools\":["
                + string.Join(",", toolNames.Select(name => $"{{\"name\":{JsonSerializer.Serialize(name)},\"description\":\"\",\"inputSchema\":{{\"type\":\"object\"}}}}"))
                + "]}}",
            _ => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":{}}",
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        };
    }
}
