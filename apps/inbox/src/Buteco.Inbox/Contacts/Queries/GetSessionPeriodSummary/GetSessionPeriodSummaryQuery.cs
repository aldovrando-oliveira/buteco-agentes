using Buteco.Inbox.Contacts.Responses;
using Mediator;

namespace Buteco.Inbox.Contacts.Queries.GetSessionPeriodSummary;

/// <summary>
/// Limites já parseados e normalizados para UTC. O parse e a validação ficam no
/// endpoint (design.md, D6 e D7) — esta query nunca recebe <c>string</c>, porque
/// um limite malformado não deve chegar até aqui para virar exceção no meio da
/// consulta.
/// </summary>
public sealed record GetSessionPeriodSummaryQuery(DateTimeOffset From, DateTimeOffset To)
    : IQuery<SessionPeriodSummaryResponse>;
