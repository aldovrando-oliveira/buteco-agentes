using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Contacts.Queries.ListContacts;

public sealed class ListContactsQueryHandler(AppDbContext dbContext) : IQueryHandler<ListContactsQuery, IReadOnlyList<ContactResponse>>
{
    public async ValueTask<IReadOnlyList<ContactResponse>> Handle(ListContactsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Contacts
            .AsNoTracking()
            .OrderBy(contact => contact.CreatedAt)
            .Select(contact => ContactResponse.FromEntity(contact))
            .ToListAsync(cancellationToken);
    }
}
