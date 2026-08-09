using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Contacts.Queries.GetContactSessions;

public sealed class GetContactSessionsQueryHandler(AppDbContext dbContext) : IQueryHandler<GetContactSessionsQuery, IReadOnlyList<SessionResponse>?>
{
    public async ValueTask<IReadOnlyList<SessionResponse>?> Handle(GetContactSessionsQuery query, CancellationToken cancellationToken)
    {
        var contactExists = await dbContext.Contacts
            .AsNoTracking()
            .AnyAsync(contact => contact.Id == query.ContactId, cancellationToken);
        if (!contactExists)
        {
            return null;
        }

        return await dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.ContactId == query.ContactId)
            .OrderBy(session => session.StartedAt)
            .Select(session => SessionResponse.FromEntity(session))
            .ToListAsync(cancellationToken);
    }
}
