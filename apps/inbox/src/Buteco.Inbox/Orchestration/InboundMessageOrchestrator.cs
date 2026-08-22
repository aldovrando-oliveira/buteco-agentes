using Buteco.Inbox.Contacts;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
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
        MessageContentType contentType,
        string externalMessageId,
        string? displayName,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken)
    {
        var session = await sessionResolver.FindOrCreateSessionAsync(channelId, externalId, contactMetadata, displayName, cancellationToken);
        await ReceiveForSessionAsync(session.Id, text, contentType, externalMessageId, receivedAt, cancellationToken);
    }

    private async Task ReceiveForSessionAsync(
        Guid sessionId,
        string text,
        MessageContentType contentType,
        string externalMessageId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        // inbox-mensagens-persistidas, design.md, Decisão 5: webhook
        // reentregue com o mesmo identificador externo não deve gerar nova
        // Message nem novo conteúdo no buffer de debounce.
        if (await IsDuplicateAsync(sessionId, externalMessageId, cancellationToken))
        {
            return;
        }

        var pendingDispatch = await FindPendingAsync(sessionId, cancellationToken);
        if (pendingDispatch is not null
            && await TryAppendWithRetryAsync(pendingDispatch, sessionId, text, contentType, externalMessageId, receivedAt, cancellationToken))
        {
            return;
        }

        pendingDispatch = new PendingDispatch(sessionId, text, receivedAt);
        dbContext.PendingDispatches.Add(pendingDispatch);
        dbContext.Messages.Add(Message.CreateInbound(sessionId, text, contentType, receivedAt, externalMessageId, pendingDispatch.Id));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Mesma corrida (e mesma mitigação) de
            // ContactSessionResolver.FindOrCreateContactAsync: outra
            // mensagem quase simultânea da mesma Session já criou a
            // PendingDispatch Pending, ou já persistiu a Message com este
            // ExternalId, entre a leitura e este SaveChanges — detach os
            // registros novos (qualquer entidade ainda Added) e reentra no
            // fluxo completo, que resolve o estado real: mensagem já
            // deduplicada, ou PendingDispatch Pending já existente para
            // anexar.
            DetachAddedEntities();

            await ReceiveForSessionAsync(sessionId, text, contentType, externalMessageId, receivedAt, cancellationToken);
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
        MessageContentType contentType,
        string externalMessageId,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        dbContext.Messages.Add(Message.CreateInbound(sessionId, text, contentType, receivedAt, externalMessageId, pendingDispatch.Id));

        for (var attempt = 0; ; attempt++)
        {
            pendingDispatch.AppendMessage(text, receivedAt);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // Mesmo ExternalId já persistido por uma chamada concorrente
                // (reentrega quase simultânea do mesmo webhook, design.md,
                // Decisão 5) — a mensagem já está registrada, nada mais a
                // fazer.
                DetachAddedEntities();
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAppendRetries)
            {
                dbContext.Entry(pendingDispatch).State = EntityState.Detached;

                var refreshed = await FindPendingAsync(sessionId, cancellationToken);
                if (refreshed is null)
                {
                    DetachAddedEntities();
                    return false;
                }

                pendingDispatch = refreshed;
            }
        }
    }

    private Task<PendingDispatch?> FindPendingAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.SessionId == sessionId && dispatch.Status == PendingDispatchStatus.Pending, cancellationToken);

    private Task<bool> IsDuplicateAsync(Guid sessionId, string externalMessageId, CancellationToken cancellationToken) =>
        dbContext.Messages.AnyAsync(
            message => message.SessionId == sessionId
                && message.Direction == MessageDirection.Inbound
                && message.ExternalId == externalMessageId,
            cancellationToken);

    private void DetachAddedEntities()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
