using System.Net;
using System.Text;
using System.Text.Json;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// Simula <c>GET /agents/{id}</c> de apps/api na camada HTTP, sem rede real
/// — injetado no lugar do <see cref="HttpMessageHandler"/> primário do
/// cliente HTTP nomeado "AgentReferenceValidator" (ver
/// <see cref="Buteco.Inbox.Agents.AgentReferenceValidator"/>), mesmo
/// mecanismo de FakeMcpServerHttpMessageHandler (apps/api). Quatro
/// comportamentos configuráveis pelo teste (design.md, Decision 4): (a)
/// HTTP 200 com um agente existente, <see cref="ExistingAgentIsActive"/>
/// configurável; (b) HTTP 404 (agente inexistente); (c) HTTP 500 (resposta
/// HTTP completa, com status de erro); (d) <see cref="SimulateUnreachable"/>
/// lança <see cref="HttpRequestException"/> (host inalcançável, sem
/// resposta HTTP nenhuma) — (c) e (d) são cenários distintos.
/// </summary>
public sealed class FakeAgentApiHttpMessageHandler : HttpMessageHandler
{
    public HashSet<Guid> ExistingAgentIds { get; } = [];

    public bool ExistingAgentIsActive { get; set; } = true;

    public bool SimulateServerError { get; set; }

    public bool SimulateUnreachable { get; set; }

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;

        if (SimulateUnreachable)
        {
            throw new HttpRequestException("Simulated unreachable host.");
        }

        if (SimulateServerError)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }

        var agentId = ExtractAgentId(request.RequestUri!);

        if (!ExistingAgentIds.Contains(agentId))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var responseJson = JsonSerializer.Serialize(new
        {
            id = agentId,
            name = "Agente de Teste",
            instructions = "Instruções de teste",
            isActive = ExistingAgentIsActive,
        });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
    }

    private static Guid ExtractAgentId(Uri requestUri) =>
        Guid.Parse(requestUri.Segments[^1]);
}
