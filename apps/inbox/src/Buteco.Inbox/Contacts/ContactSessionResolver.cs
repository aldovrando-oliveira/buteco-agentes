using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Buteco.Inbox.Contacts;

public sealed class ContactSessionResolver(AppDbContext dbContext, IOptions<SessionOptions> options) : IContactSessionResolver
{
    public async Task<Session> FindOrCreateSessionAsync(
        Guid channelId,
        string externalId,
        IReadOnlyDictionary<string, string> contactMetadata,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var contact = await FindOrCreateContactAsync(channelId, externalId, contactMetadata, displayName, cancellationToken);

        try
        {
            return await TryResolveSessionOnceAsync(contact.Id, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Outra chamada concorrente já fechou a Session anterior e/ou
            // criou a nova Session aberta deste Contact entre a leitura e
            // este SaveChanges — detach de Added (a Session nova desta
            // tentativa) e Modified (o Close() da Session expirando,
            // revertido junto pelo rollback da transação malsucedida;
            // diferente de InboundMessageOrchestrator.DetachAddedEntities(),
            // que só cobre Added porque seu cenário não faz update na mesma
            // transação) e re-busca uma única vez — não em loop: o Postgres
            // só libera esta exceção para quem perde a corrida depois que o
            // vencedor já commitou, então a retentativa sempre encontra a
            // Session vencedora já persistida (design.md, "Por que uma
            // retentativa basta").
            DetachAddedAndModifiedEntities();

            return await TryResolveSessionOnceAsync(contact.Id, cancellationToken);
        }
    }

    private async Task<Session> TryResolveSessionOnceAsync(Guid contactId, CancellationToken cancellationToken)
    {
        // ClosedAt == null: o índice único é a única fonte de verdade sobre
        // "aberta" — a busca não depende do invariante implícito "mais
        // recente por StartedAt é sempre a aberta", que deixaria de valer no
        // dia em que existir encerramento explícito de sessão (item em
        // aberto, 02-HISTORICO_E_STATUS.md).
        var session = await dbContext.Sessions
            .Where(existing => existing.ContactId == contactId && existing.ClosedAt == null)
            .OrderByDescending(existing => existing.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var timeout = options.Value.InactivityTimeout;
        if (session is null || DateTimeOffset.UtcNow - session.LastActivityAt > timeout)
        {
            session?.Close(DateTimeOffset.UtcNow);

            session = new Session(contactId);
            dbContext.Sessions.Add(session);
        }
        else
        {
            session.RegisterActivity();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return session;
    }

    private void DetachAddedAndModifiedEntities()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task<Contact> FindOrCreateContactAsync(
        Guid channelId,
        string externalId,
        IReadOnlyDictionary<string, string> contactMetadata,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Contacts
            .FirstOrDefaultAsync(contact => contact.ChannelId == channelId && contact.ExternalId == externalId, cancellationToken);
        if (existing is not null)
        {
            // contactMetadata desta chamada é ignorado — Contact já existe,
            // metadado gravado só na criação (inbox-adapter-waha, design.md,
            // Decision 8). displayName, ao contrário, é atualizado a cada
            // chamada — persistido pelo SaveChangesAsync de
            // FindOrCreateSessionAsync (inbox-mensagens-persistidas,
            // design.md, Decisão 9).
            existing.UpdateDisplayName(displayName);
            return existing;
        }

        var contact = new Contact(channelId, externalId, contactMetadata, displayName);
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

            var raced = await dbContext.Contacts
                .FirstAsync(other => other.ChannelId == channelId && other.ExternalId == externalId, cancellationToken);
            raced.UpdateDisplayName(displayName);
            return raced;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
