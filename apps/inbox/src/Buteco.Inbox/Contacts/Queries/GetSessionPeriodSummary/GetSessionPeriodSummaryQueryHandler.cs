using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Contacts.Queries.GetSessionPeriodSummary;

/// <summary>
/// Uma consulta agregada, sempre — <b>o custo não cresce com o número de
/// sessões</b>, no molde de <c>GetKnowledgeBaseIndexingSummaryQueryHandler</c>
/// (<c>apps/api</c>), que é o único agregado desta base (design.md, D1).
/// </summary>
/// <remarks>
/// <c>CountAsync</c> traduz para <c>SELECT COUNT(*)</c>: nunca
/// <c>Select().ToList().Count()</c>, que sai com o resultado <b>certo</b> e o
/// custo errado — e passa despercebido exatamente por isso.
///
/// O filtro é por <see cref="Entities.Session.StartedAt"/>, e a escolha entre as
/// três definições possíveis de "sessão no período" está em design.md, D3.
/// Resumo do que trava a decisão aqui: <c>LastActivityAt</c> é reescrito a cada
/// mensagem, então o número de um período passado mudaria depois de medido;
/// <c>ClosedAt IS NULL</c> não significa "aberta", significa "ninguém voltou a
/// escrever" (ver <c>02-HISTORICO_E_STATUS.md</c>, item "Encerramento explícito
/// de sessão"). <c>StartedAt</c> é atribuído no construtor e nunca reescrito —
/// é a única das três que particiona.
///
/// Sem índice em <c>StartedAt</c>, e isso é decisão com gatilho, não esquecimento
/// (design.md, D9): a 10 sessões em dev o Postgres faz seq scan e ignoraria o
/// índice de qualquer forma. Recalibrar junto com o teto de intervalo (D8), no
/// primeiro deploy em produção com volume real.
/// </remarks>
public sealed class GetSessionPeriodSummaryQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetSessionPeriodSummaryQuery, SessionPeriodSummaryResponse>
{
    public async ValueTask<SessionPeriodSummaryResponse> Handle(
        GetSessionPeriodSummaryQuery query, CancellationToken cancellationToken)
    {
        // Inclusivo nos DOIS limites (design.md, D3): >= e <=, nunca > ou <.
        // Uma sessão com StartedAt exatamente igual a From, ou exatamente igual
        // a To, conta.
        var startedCount = await dbContext.Sessions
            .AsNoTracking()
            .CountAsync(
                session => session.StartedAt >= query.From && session.StartedAt <= query.To,
                cancellationToken);

        return new SessionPeriodSummaryResponse(startedCount);
    }
}
