using Buteco.Inbox.Messages.Responses;
using Mediator;

namespace Buteco.Inbox.Messages.Queries.GetMessagePeriodSummary;

/// <summary>
/// Limites já parseados e normalizados para UTC. O parse e a validação ficam no
/// endpoint (design.md, D8) — esta query nunca recebe <c>string</c>, porque um
/// limite malformado não deve chegar até aqui para virar exceção no meio da
/// consulta.
/// </summary>
public sealed record GetMessagePeriodSummaryQuery(DateTimeOffset From, DateTimeOffset To)
    : IQuery<MessagePeriodSummaryResponse>;
