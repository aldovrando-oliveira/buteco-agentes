using System.Net;
using System.Text;
using System.Text.Json;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Simula um servidor MCP remoto na camada HTTP, sem rede real — injetado no
/// lugar do <see cref="HttpMessageHandler"/> primário do cliente HTTP nomeado
/// "McpConnectionTester" (ver <see cref="Buteco.Api.McpServers.Connectivity.McpConnectionTester"/>).
/// Responde ao handshake `initialize` do protocolo MCP (Streamable HTTP)
/// com sucesso, opcionalmente exigindo um Bearer token específico, ou
/// simula um host inalcançável lançando <see cref="HttpRequestException"/>.
/// </summary>
public sealed class FakeMcpServerHttpMessageHandler : HttpMessageHandler
{
    public bool SimulateUnreachable { get; set; }

    public string? RequiredBearerToken { get; set; }

    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;

        if (SimulateUnreachable)
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

        if (method == "initialize")
        {
            var id = root.GetProperty("id").GetRawText();
            var responseJson = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"serverInfo\":{\"name\":\"fake-mcp-server\",\"version\":\"1.0.0\"}}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }

        // Notificações (ex.: notifications/initialized) não esperam corpo de resposta.
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    }
}
