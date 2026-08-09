using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Buteco.Inbox.Contacts;

public sealed class ContactSessionResolver(AppDbContext dbContext, IOptions<SessionOptions> options) : IContactSessionResolver
{
    public async Task<Session> FindOrCreateSessionAsync(Guid channelId, string externalId, CancellationToken cancellationToken)
    {
        var contact = await FindOrCreateContactAsync(channelId, externalId, cancellationToken);

        var session = await dbContext.Sessions
            .Where(existing => existing.ContactId == contact.Id)
            .OrderByDescending(existing => existing.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var timeout = options.Value.InactivityTimeout;
        if (session is null || DateTimeOffset.UtcNow - session.LastActivityAt > timeout)
        {
            session = new Session(contact.Id);
            dbContext.Sessions.Add(session);
        }
        else
        {
            session.RegisterActivity();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return session;
    }

    private async Task<Contact> FindOrCreateContactAsync(Guid channelId, string externalId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Contacts
            .FirstOrDefaultAsync(contact => contact.ChannelId == channelId && contact.ExternalId == externalId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var contact = new Contact(channelId, externalId);
        dbContext.Contacts.Add(contact);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return contact;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // O EF Core não reverte automaticamente o change tracker depois
            // de um SaveChangesAsync malsucedido — sem o detach explícito, o
            // SaveChangesAsync seguinte (o da Session, mesmo DbContext
            // Scoped) tentaria reinserir esse mesmo Contact ainda rastreado
            // como Added, causando uma segunda violação sem captura
            // (design.md, Decision 7).
            dbContext.Entry(contact).State = EntityState.Detached;

            return await dbContext.Contacts
                .FirstAsync(other => other.ChannelId == channelId && other.ExternalId == externalId, cancellationToken);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
