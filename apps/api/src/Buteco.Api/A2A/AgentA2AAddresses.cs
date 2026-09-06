using Buteco.Api.Options;

namespace Buteco.Api.A2A;

/// <summary>
/// Os dois endereços públicos A2A de um agente: o endpoint de execução e o
/// card de descoberta.
/// </summary>
public sealed record AgentA2AAddresses(string Url, string AgentCardUrl);

/// <summary>
/// Ponto único de montagem dos endereços A2A. Existe porque dois lugares
/// precisam da mesma URL — o card de descoberta, que a anuncia, e a resposta
/// de agente, que a entrega ao painel — e divergirem seria a falha mais
/// provável e a mais difícil de perceber (design.md da change
/// agente-enderecos-a2a, D1).
/// </summary>
public static class AgentA2AAddressBuilder
{
    /// <summary>
    /// Devolve <c>null</c> quando a URL pública não está configurada, em vez de
    /// montar endereço relativo. Sem a configuração não existe endereço
    /// público, e afirmar um quebrado é pior que declarar a ausência (D2).
    /// </summary>
    public static AgentA2AAddresses? Build(PublicUrlOptions options, Guid agentId)
    {
        var baseUrl = options.BaseUrl?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return new AgentA2AAddresses(
            $"{baseUrl}/agents/{agentId}/a2a",
            $"{baseUrl}/agents/{agentId}/.well-known/agent-card.json");
    }
}
