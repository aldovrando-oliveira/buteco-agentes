using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Messages.Queries.GetSessionMessages;

public sealed class GetSessionMessagesQueryHandler(AppDbContext dbContext) : IQueryHandler<GetSessionMessagesQuery, IReadOnlyList<MessageResponse>?>
{
    public async ValueTask<IReadOnlyList<MessageResponse>?> Handle(GetSessionMessagesQuery query, CancellationToken cancellationToken)
    {
        var sessionExists = await dbContext.Sessions
            .AsNoTracking()
            .AnyAsync(session => session.Id == query.SessionId, cancellationToken);
        if (!sessionExists)
        {
            return null;
        }

        return await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == query.SessionId)
            .OrderBy(message => message.OccurredAt)
            .Select(message => MessageResponse.FromEntity(message))
            .ToListAsync(cancellationToken);
    }
}
