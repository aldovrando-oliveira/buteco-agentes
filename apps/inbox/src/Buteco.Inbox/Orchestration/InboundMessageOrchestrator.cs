using Buteco.Inbox.Contacts;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Inbox.Orchestration;

public sealed class InboundMessageOrchestrator(
    AppDbContext dbContext,
    IContactSessionResolver sessionResolver) : IInboundMessageOrchestrator
{
    public async Task ReceiveMessageAsync(
        Guid channelId,
        string externalId,
        string text,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken)
    {
        var session = await sessionResolver.FindOrCreateSessionAsync(channelId, externalId, contactMetadata, cancellationToken);

        var pendingDispatch = await FindPendingAsync(session.Id, cancellationToken);
        if (pendingDispatch is not null)
        {
            pendingDispatch.AppendMessage(text, receivedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        pendingDispatch = new PendingDispatch(session.Id, text, receivedAt);
        dbContext.PendingDispatches.Add(pendingDispatch);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Mesma corrida (e mesma mitigação) de
            // ContactSessionResolver.FindOrCreateContactAsync: outra
            // mensagem quase simultânea da mesma Session já criou a
            // PendingDispatch Pending entre a leitura e este SaveChanges —
            // detach o registro rejeitado e reaproveita o já existente.
            dbContext.Entry(pendingDispatch).State = EntityState.Detached;

            var existing = await FindPendingAsync(session.Id, cancellationToken)
                ?? throw new InvalidOperationException("Violação de unicidade sem PendingDispatch Pending correspondente.");
            existing.AppendMessage(text, receivedAt);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Task<PendingDispatch?> FindPendingAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.SessionId == sessionId && dispatch.Status == PendingDispatchStatus.Pending, cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
