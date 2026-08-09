using Buteco.Inbox.Contacts.Responses;
using Mediator;

namespace Buteco.Inbox.Contacts.Queries.ListContacts;

public sealed record ListContactsQuery : IQuery<IReadOnlyList<ContactResponse>>;
