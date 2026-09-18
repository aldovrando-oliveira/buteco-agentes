using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Messages.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Messages.Queries.GetMessagePeriodSummary;

/// <summary>
/// Uma consulta agregada, sempre — <b>o custo não cresce com o número de
/// mensagens</b>, no molde de <c>GetSessionPeriodSummaryQueryHandler</c> e, antes
/// dele, <c>GetKnowledgeBaseIndexingSummaryQueryHandler</c> (<c>apps/api</c>)
/// (design.md, D1).
/// </summary>
/// <remarks>
/// <c>CountAsync</c> traduz para <c>SELECT COUNT(*)</c>: nunca
/// <c>Select().ToList().Count()</c>, que sai com o resultado <b>certo</b> e o
/// custo errado — e passa despercebido exatamente por isso.
///
/// <b>Não há tabela de três definições aqui, e a ausência é decisão</b>
/// (design.md, D3). O vizinho de sessão precisou descartar duas de três leituras
/// possíveis; <see cref="Message"/> tem <b>um</b> campo temporal, e ele é
/// write-once: <see cref="Message.OccurredAt"/> é atribuído em
/// <see cref="Message.CreateInbound"/> e <see cref="Message.CreateOutbound"/> e
/// em nenhum outro lugar, e o único mutador público da entidade
/// (<see cref="Message.UpdateDispatchStatus"/>) não o toca. Não há definição
/// concorrente a eliminar.
///
/// <b>Mas o nome mente, e isso é gatilho registrado</b> (design.md, D5):
/// <c>OccurredAt</c> é o instante de <b>recebimento pelo servidor</b>
/// (<c>DateTimeOffset.UtcNow</c> no adapter — <c>TelegramInboundWebhookHandler.cs:105</c>,
/// <c>WahaInboundWebhookHandler.cs:64</c>), não o instante que o provedor
/// registrou no evento. É isso que torna esta contagem estável — nenhum provedor
/// consegue inserir mensagem "no passado". Se uma change futura passar a parsear
/// o campo de instante do provedor para dentro deste campo, a rota se torna
/// retroativamente backdatable <b>sem ninguém tocar este código</b>.
///
/// <b>Sem índice novo, e a razão é DIFERENTE da do vizinho de sessão</b>
/// (design.md, D10). Lá a afirmação era "não há índice em <c>StartedAt</c>". Aqui
/// <b>existe</b> <c>IX_messages_SessionId_OccurredAt</c>
/// (<c>AppDbContext.cs:164</c>), mas com <c>SessionId</c> como coluna <b>líder</b>:
/// esta consulta não restringe <c>SessionId</c>, então não há range seek, e
/// <c>Direction</c> nem está no índice. O índice existe e não serve a esta
/// consulta — escrever "não há índice em <c>OccurredAt</c>" seria falso.
/// Recalibrar com o teto de intervalo, no primeiro volume real; e note que
/// <c>messages</c> cresce por um múltiplo de <c>sessions</c>.
/// </remarks>
public sealed class GetMessagePeriodSummaryQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetMessagePeriodSummaryQuery, MessagePeriodSummaryResponse>
{
    public async ValueTask<MessagePeriodSummaryResponse> Handle(
        GetMessagePeriodSummaryQuery query, CancellationToken cancellationToken)
    {
        // Os DOIS predicados. Direction primeiro porque é o que distingue esta
        // contagem de "mensagens no período" (design.md, D6): sem ele a rota
        // contaria também as respostas do agente, e a suíte só reprova no
        // [Fact] de 4.5 — que existe exatamente para isso.
        //
        // Inclusivo nos DOIS limites: >= e <=, nunca > ou <. Uma mensagem com
        // OccurredAt exatamente igual a From, ou exatamente igual a To, conta.
        var inboundCount = await dbContext.Messages
            .AsNoTracking()
            .CountAsync(
                message => message.Direction == MessageDirection.Inbound
                    && message.OccurredAt >= query.From
                    && message.OccurredAt <= query.To,
                cancellationToken);

        return new MessagePeriodSummaryResponse(inboundCount);
    }
}
