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
    // Pior caso teórico de serialização sob N chamadas concorrentes para a
    // mesma Session: até N-1 rodadas de retry para a última chamada vencer
    // o UPDATE condicionado a xmin (design.md, Decisão 2). Proteção de
    // robustez local, não um conceito de negócio configurável como
    // DebounceOptions.MaxDispatchAttempts.
    private const int MaxAppendRetries = 10;

    public async Task ReceiveMessageAsync(
        Guid channelId,
        string externalId,
        string text,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken)
    {
        var session = await sessionResolver.FindOrCreateSessionAsync(channelId, externalId, contactMetadata, cancellationToken);
        await ReceiveForSessionAsync(session.Id, text, receivedAt, cancellationToken);
    }

    private async Task ReceiveForSessionAsync(
        Guid sessionId,
        string text,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var pendingDispatch = await FindPendingAsync(sessionId, cancellationToken);
        if (pendingDispatch is not null
            && await TryAppendWithRetryAsync(pendingDispatch, sessionId, text, receivedAt, cancellationToken))
        {
            return;
        }

        pendingDispatch = new PendingDispatch(sessionId, text, receivedAt);
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

            var existing = await FindPendingAsync(sessionId, cancellationToken)
                ?? throw new InvalidOperationException("Violação de unicidade sem PendingDispatch Pending correspondente.");

            if (!await TryAppendWithRetryAsync(existing, sessionId, text, receivedAt, cancellationToken))
            {
                // A linha encontrada acima já deixou de estar Pending antes
                // do append (reivindicada pelo DebounceSweepService ou
                // removida) — mesmo tratamento do caminho principal acima:
                // reentra no fluxo completo, que vai encontrar "nenhuma
                // Pending" e criar uma nova PendingDispatch.
                await ReceiveForSessionAsync(sessionId, text, receivedAt, cancellationToken);
            }
        }
    }

    // Anexa `text` a uma PendingDispatch Pending já encontrada, retentando
    // sob DbUpdateConcurrencyException (xmin, design.md Decisão 5 de
    // inbox-orquestrador-debounce): várias chamadas concorrentes podem ter
    // lido a mesma versão da linha antes de qualquer uma commitar (design.md
    // desta change, Decisão 1). A cada conflito, detacha a entidade e
    // rebusca via FindPendingAsync — não usa dbContext.Entry(...).ReloadAsync
    // de propósito: para a coleção owned/JSON Messages (ToJson()), Reload
    // pôde deixar o snapshot "original" do change tracker apontando para a
    // MESMA instância de List usada como valor "current", mascarando o
    // AppendMessage seguinte do change tracker — SaveChangesAsync retornava
    // sucesso (sem exceção) sem de fato persistir a mensagem (reproduzido:
    // ReceiveMessageAsync_ConcurrentCallsExistingPendingDispatch_AppendsAllMessagesWithoutLoss
    // perdia 1 de 9 mensagens de forma intermitente só quando o retry usava
    // ReloadAsync). Rebuscar via FindPendingAsync força uma materialização
    // nova via LINQ, o mesmo caminho já usado em todo o resto deste arquivo.
    // Retorna false se não há mais PendingDispatch Pending para reaproveitar
    // — reivindicada pelo DebounceSweepService ou removida — o chamador deve
    // tratar como "nenhuma Pending disponível" e cair no caminho de criação.
    private async Task<bool> TryAppendWithRetryAsync(
        PendingDispatch pendingDispatch,
        Guid sessionId,
        string text,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            pendingDispatch.AppendMessage(text, receivedAt);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAppendRetries)
            {
                dbContext.Entry(pendingDispatch).State = EntityState.Detached;

                var refreshed = await FindPendingAsync(sessionId, cancellationToken);
                if (refreshed is null)
                {
                    return false;
                }

                pendingDispatch = refreshed;
            }
        }
    }

    private Task<PendingDispatch?> FindPendingAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.SessionId == sessionId && dispatch.Status == PendingDispatchStatus.Pending, cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
