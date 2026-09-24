using Buteco.Api.Insights.Responses;
using Mediator;

namespace Buteco.Api.Insights.Queries.GetAgentInsights;

/// <summary>
/// Gêmeo de <c>GetSystemInsightsQuery</c>, com o identificador do agente a mais —
/// e os dois limites chegam aqui já interpretados e normalizados para
/// deslocamento zero, nunca como <c>string</c>.
///
/// <para>
/// <b>A resposta é anulável, e isso é o contrato do <c>404</c></b> (design.md,
/// D3): nulo significa que o identificador não corresponde a agente algum. Um
/// agente que existe e não tem dado no período devolve resposta <b>preenchida</b>,
/// com contagens <c>0</c> — período medido e vazio é outra coisa, e colapsar os
/// dois seria o quarto estado da convenção 13 ("sei que não existe") caindo no
/// segundo.
/// </para>
/// </summary>
public sealed record GetAgentInsightsQuery(Guid AgentId, DateTimeOffset From, DateTimeOffset To)
    : IQuery<AgentInsightsResponse?>;
