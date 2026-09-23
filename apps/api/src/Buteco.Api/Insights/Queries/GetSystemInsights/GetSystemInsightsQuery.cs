using Buteco.Api.Insights.Responses;
using Mediator;

namespace Buteco.Api.Insights.Queries.GetSystemInsights;

/// <summary>
/// Os dois limites chegam aqui já interpretados e normalizados para
/// deslocamento zero — nunca como <c>string</c>. Quem interpreta é
/// <see cref="InsightsPeriod"/>, no endpoint: a query recebe instantes, e a
/// diferença importa porque é o que garante que um valor malformado já virou
/// resposta de validação antes de qualquer consulta ao banco.
/// </summary>
public sealed record GetSystemInsightsQuery(DateTimeOffset From, DateTimeOffset To)
    : IQuery<SystemInsightsResponse>;
