using A2A;
using Buteco.Inbox.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Orchestration;

// A2A.A2AClient do pacote A2A já referenciado, não JSON-RPC manual nem
// Microsoft.Agents.AI/AIAgent completo (design.md, Decisão 3). Uma
// instância por chamada — A2AClient só embrulha o Uri + HttpClient, sem
// custo de setup a amortizar.
public sealed class A2AClientFactory(IHttpClientFactory httpClientFactory, IOptions<ApiOptions> apiOptions) : IA2AClientFactory
{
    public const string HttpClientName = "A2AClient";

    public IA2AClient CreateForAgent(Guid agentId)
    {
        var baseUrl = apiOptions.Value.BaseUrl
            ?? throw new InvalidOperationException("Api:BaseUrl não configurado.");

        var client = httpClientFactory.CreateClient(HttpClientName);
        return new A2AClient(new Uri($"{baseUrl}/agents/{agentId}/a2a"), client);
    }
}
