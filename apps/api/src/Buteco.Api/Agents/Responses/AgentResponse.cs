using System.Text.Json.Serialization;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Entities;
using Buteco.Api.McpServers.Responses;

namespace Buteco.Api.Agents.Responses;

public record AgentResponse(
    Guid Id,
    string Name,
    string Instructions,
    bool IsActive,
    string? Provider,
    string? Model,
    string? Description,
    IReadOnlyList<SkillResponse> Skills,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<McpServerSummaryResponse> McpServers,
    IReadOnlyList<AgentSummaryResponse> DelegatesTo,
    // Endereços públicos A2A do agente, montados no servidor a partir da URL
    // pública configurada. Nulo quando ela não está configurada: sem ela não
    // existe endereço público, e devolver um relativo faria o painel montar a
    // URL a partir do host de onde a página foi servida — que atrás de proxy
    // não é o host da API (design.md da change agente-enderecos-a2a, D1 e D2).
    //
    // Não há campo dizendo se A2A está habilitado: todo agente tem os dois
    // endereços, sempre, e o que varia é ele aceitar trabalho — que é o
    // IsActive logo acima (D3).
    // O nome no fio é fixado explicitamente porque a política camelCase do
    // System.Text.Json minúscula só a primeira letra: `A2A` viraria `a2A`, e o
    // painel leria `a2a` como ausente. Os testes que desserializam para este
    // record são cegos a isso — passam pela mesma política nos dois sentidos.
    [property: JsonPropertyName("a2a")] AgentA2AAddresses? A2A)
{
    public static AgentResponse FromEntity(
        Agent agent,
        IReadOnlyList<McpServerSummaryResponse> mcpServers,
        IReadOnlyList<AgentSummaryResponse> delegatesTo,
        AgentA2AAddresses? a2a) =>
        new(
            agent.Id,
            agent.Name,
            agent.Instructions,
            agent.IsActive,
            agent.Provider,
            agent.Model,
            agent.Description,
            agent.Skills.Select(SkillResponse.FromEntity).ToList(),
            agent.CreatedAt,
            agent.UpdatedAt,
            mcpServers,
            delegatesTo,
            a2a);
}
