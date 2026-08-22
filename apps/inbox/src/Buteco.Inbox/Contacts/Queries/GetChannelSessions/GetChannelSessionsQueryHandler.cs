using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Contacts.Queries.GetChannelSessions;

// Prévia da última mensagem via subquery correlacionada por Session,
// servida pelo mesmo índice (SessionId, OccurredAt) já dimensionado para
// GetSessionMessagesQuery — nenhum índice novo (inbox-mensagens-persistidas,
// design.md, Decisões 7 e 10).
public sealed class GetChannelSessionsQueryHandler(AppDbContext dbContext) : IQueryHandler<GetChannelSessionsQuery, IReadOnlyList<ChannelSessionResponse>?>
{
    public async ValueTask<IReadOnlyList<ChannelSessionResponse>?> Handle(GetChannelSessionsQuery query, CancellationToken cancellationToken)
    {
        var channelExists = await dbContext.Channels
            .AsNoTracking()
            .AnyAsync(channel => channel.Id == query.ChannelId, cancellationToken);
        if (!channelExists)
        {
            return null;
        }

        return await (
            from session in dbContext.Sessions.AsNoTracking()
            join contact in dbContext.Contacts.AsNoTracking() on session.ContactId equals contact.Id
            where contact.ChannelId == query.ChannelId
            orderby session.LastActivityAt descending
            select new ChannelSessionResponse(
                session.Id,
                contact.Id,
                contact.ExternalId,
                contact.DisplayName,
                session.LastActivityAt,
                dbContext.Messages
                    .Where(message => message.SessionId == session.Id)
                    .OrderByDescending(message => message.OccurredAt)
                    .Select(message => new MessagePreviewResponse(message.Direction, message.Content, message.OccurredAt))
                    .FirstOrDefault())
        ).ToListAsync(cancellationToken);
    }
}
