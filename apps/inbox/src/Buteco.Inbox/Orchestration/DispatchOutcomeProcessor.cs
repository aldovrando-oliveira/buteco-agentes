using A2A;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.EntityFrameworkCore;
// A2A traz A2A.Message — alias explícito para não colidir com a entidade.
using MessageEntity = Buteco.Inbox.Messages.Entities.Message;

namespace Buteco.Inbox.Orchestration;

public enum DispatchOutcome
{
    Resolved,

    // Outro caminho resolveu a mesma linha antes da gravação final (xmin).
    AlreadyResolved,
}

/// <summary>
/// O desfecho de um ciclo de disparo, decidido num lugar só para os três
/// caminhos que encerram um ciclo: a push notification, a reconciliação e as
/// falhas do lado do inbox no <see cref="DebounceSweepService"/> (design.md da
/// change pending-dispatch-orfa, D5). A reconciliação não tem semântica própria:
/// ela entrega a mesma <see cref="AgentTask"/> que o push entregaria.
/// </summary>
/// <remarks>
/// <para>
/// <b>A entrega usa o token do chamador; a gravação, não.</b> O endpoint de push
/// passa <c>CancellationToken.None</c> (o worker aborta a conexão em 5 s, e isso
/// não pode desfazer uma entrega já feita). O debounce e a reconciliação passam
/// o prazo da parada (D11): uma entrega ao canal que passe dele é cancelada, o
/// cancelamento sobe, e a linha fica em Dispatching para o boot seguinte. A
/// gravação final, curta, roda sempre com
/// <c>CancellationToken.None</c>: uma entrega feita e não registrada é o pior
/// resultado possível, porque a linha volta a ser reconciliada e a resposta sai
/// em dobro.
/// </para>
/// </remarks>
public sealed class DispatchOutcomeProcessor(
    AppDbContext dbContext,
    IChannelCredentialCipher credentialCipher,
    IChannelAdapterRegistry adapterRegistry,
    ILogger<DispatchOutcomeProcessor> logger)
{
    // Texto fixado pelo dono em 03/10/2026, igual para todo canal e agente, e
    // entregue em todo desfecho em que nenhuma resposta virá por falha —
    // inclusive a rejeição síncrona por agente inativo (design.md, D6). Sem
    // configuração: não há cenário real de outro valor (convenção 2).
    public const string FailureNoticeText = "Não consegui responder agora. Pode tentar de novo em instantes?";

    /// <summary>
    /// Encerra o ciclo pelo estado terminal da task: a resposta, quando a task
    /// concluiu com texto; nada, quando concluiu sem texto; o aviso de falha, em
    /// qualquer outro estado terminal.
    /// </summary>
    public async Task<DispatchOutcome> CompleteFromTaskAsync(
        PendingDispatch pendingDispatch, AgentTask task, CancellationToken deliveryToken)
    {
        if (task.Status.State == TaskState.Completed)
        {
            var responseText = ExtractResponseText(task);
            if (responseText is not null)
            {
                await DeliverAsync(pendingDispatch, responseText, deliveryToken);
            }

            return await CloseAsync(pendingDispatch, MessageDispatchStatus.Completed, task.Id);
        }

        await DeliverAsync(pendingDispatch, FailureNoticeText, deliveryToken);
        return await CloseAsync(pendingDispatch, MessageDispatchStatus.Failed, task.Id);
    }

    /// <summary>
    /// Encerra o ciclo por uma falha do lado do inbox — rejeição síncrona,
    /// rejeição de protocolo, esgotamento de tentativas, ou linha sem TaskId
    /// além do limite: aviso de falha e mensagens de entrada em Failed.
    /// </summary>
    public async Task<DispatchOutcome> FailAsync(PendingDispatch pendingDispatch, CancellationToken deliveryToken)
    {
        await DeliverAsync(pendingDispatch, FailureNoticeText, deliveryToken);
        return await CloseAsync(pendingDispatch, MessageDispatchStatus.Failed, pendingDispatch.TaskId);
    }

    private async Task<DispatchOutcome> CloseAsync(PendingDispatch pendingDispatch, MessageDispatchStatus status, string? taskId)
    {
        var messages = await dbContext.Messages
            .Where(message => message.PendingDispatchId == pendingDispatch.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var message in messages)
        {
            message.UpdateDispatchStatus(status);
        }

        dbContext.Remove(pendingDispatch);

        try
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
            return DispatchOutcome.Resolved;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Resíduo 1 (design.md, D4): a outra ponta — o push ou a
            // reconciliação — reivindicou ou removeu a linha entre a leitura e
            // esta gravação. A entrega deste lado pode ter saído em dobro, e o
            // registro dela se perde com a gravação revertida; o que fica é o da
            // outra ponta. Warning e não Error: é o caminho esperado da
            // concorrência, raro por construção (exige passar da carência).
            logger.LogWarning(
                "Desfecho do disparo da sessão {SessionId} (task {TaskId}) já tinha sido resolvido por outro caminho; a entrega ao canal pode ter acontecido em dobro.",
                pendingDispatch.SessionId,
                taskId);
            return DispatchOutcome.AlreadyResolved;
        }
    }

    // A resposta do agente chega como Artifact, não como Status.Message —
    // AgentExecutionService.ExecuteAsync (apps/workers) grava o texto via
    // TaskUpdater.AddArtifactAsync e chama CompleteAsync() sem mensagem
    // final, mesmo padrão de leitura usado por AgentDelegationToolSetResolver
    // e pelos testes de apps/workers (task.Artifacts?.LastOrDefault()?.Parts).
    // Partes não textuais (Raw/Url/Data) são ignoradas nesta fatia
    // (design.md, Decision 3) — null quando não há nenhum texto a entregar.
    private static string? ExtractResponseText(AgentTask task)
    {
        var parts = task.Artifacts?.LastOrDefault()?.Parts;
        if (parts is null)
        {
            return null;
        }

        var textParts = parts.Where(part => part.Text is not null).Select(part => part.Text!).ToList();
        return textParts.Count > 0 ? string.Join('\n', textParts) : null;
    }

    private async Task DeliverAsync(PendingDispatch pendingDispatch, string text, CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;

        try
        {
            // Mesmo join de DebounceSweepService.TryDispatchAsync
            // (Session.ContactId → Contact.ChannelId → Channel), selecionando
            // os campos do Channel/Contact necessários para a entrega em vez
            // de AgentId. Dentro do try (inbox-push-notification-decrypt-resiliente,
            // design.md, Decisão 1): antes, uma falha nesta consulta escapava
            // da entrega sem deixar o PendingDispatch ser resolvido.
            var dispatchInfo = await (
                from session in dbContext.Sessions
                join contact in dbContext.Contacts on session.ContactId equals contact.Id
                join channel in dbContext.Channels on contact.ChannelId equals channel.Id
                where session.Id == pendingDispatch.SessionId
                select new { channel.Id, channel.ChannelType, channel.EncryptedCredentials, contact.ExternalId }
            ).FirstAsync(cancellationToken);

            // O processador decifra, não o sender — simétrico a como o validador
            // recebe texto plano antes de cifrar na entrada (design.md,
            // Decision 3). IOutboundMessageSender não depende de
            // IChannelCredentialCipher. Decrypt também dentro do try pelo
            // mesmo motivo da consulta acima.
            var outboundMessage = new OutboundMessage(
                dispatchInfo.Id,
                credentialCipher.Decrypt(dispatchInfo.EncryptedCredentials),
                dispatchInfo.ExternalId,
                text);

            var sender = adapterRegistry.GetOutboundMessageSender(dispatchInfo.ChannelType);
            await sender.SendAsync(outboundMessage, cancellationToken);
            dbContext.Messages.Add(MessageEntity.CreateOutbound(
                pendingDispatch.SessionId, text, occurredAt, MessageDeliveryStatus.Sent, deliveryFailureReason: null));
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Falha ao resolver o canal, decifrar a credencial ou entregar ao
            // sender é só logada — sem retry, sem nova reapresentação do
            // PendingDispatch, que já será removido (design.md, Decision 3,
            // Risks). A partir de inbox-mensagens-persistidas, também vira
            // estado persistido em Message, não só log (design.md, Decisão 4).
            // Identificado por SessionId, não por ChannelId/ChannelType —
            // dispatchInfo pode não existir se a própria consulta acima falhou
            // (inbox-push-notification-decrypt-resiliente, design.md,
            // Decisão 2).
            //
            // O cancelamento pelo prazo da parada (#47, D11) fica FORA deste
            // catch e sobe: a linha não pode ser removida com uma entrega que
            // talvez não tenha saído. Ela continua reivindicada em Dispatching e
            // é reconciliada de novo no boot seguinte.
            logger.LogError(
                exception,
                "Falha ao entregar ao canal de origem da sessão {SessionId}",
                pendingDispatch.SessionId);
            dbContext.Messages.Add(MessageEntity.CreateOutbound(
                pendingDispatch.SessionId, text, occurredAt, MessageDeliveryStatus.Failed, exception.Message));
        }
    }
}
