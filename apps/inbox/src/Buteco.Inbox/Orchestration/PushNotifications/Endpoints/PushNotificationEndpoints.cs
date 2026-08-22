using A2A;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
// A2A traz A2A.Message (usado via task.Status.Message) — alias explícito
// para não colidir com Buteco.Inbox.Messages.Entities.Message.
using MessageEntity = Buteco.Inbox.Messages.Entities.Message;

namespace Buteco.Inbox.Orchestration.PushNotifications.Endpoints;

// Recebe a AgentTask completa quando uma task disparada por
// DebounceSweepService atinge um estado terminal (design.md, Decisão 6).
// Nome do header idêntico ao já usado por PushNotificationSender
// (apps/workers) — não um valor novo.
public static class PushNotificationEndpoints
{
    public const string RoutePattern = "/internal/push-notifications";

    public const string TokenHeaderName = "X-A2A-Notification-Token";

    public static IEndpointRouteBuilder MapPushNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(RoutePattern, ReceiveAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.PreExistingAuthMechanism));

        return app;
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>> ReceiveAsync(
        AgentTask task,
        HttpRequest request,
        AppDbContext dbContext,
        IChannelCredentialCipher credentialCipher,
        IChannelAdapterRegistry adapterRegistry,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // PushNotificationEndpoints é estática — não pode ser argumento de
        // tipo de ILogger<T>, daí ILoggerFactory + nome explícito.
        var logger = loggerFactory.CreateLogger(typeof(PushNotificationEndpoints).FullName!);

        var token = request.Headers[TokenHeaderName].FirstOrDefault();

        // Só usada para a leitura inicial — daqui em diante o processamento
        // roda com CancellationToken.None, não com o token da requisição
        // (ligado a HttpContext.RequestAborted). PushNotificationSender
        // (apps/workers) usa um HttpClient com Timeout curto e fixo de 5s
        // (Program.cs, design.md, Decision 3) e nunca espera nem reage à
        // resposta deste endpoint — se o round-trip (chamar o canal +
        // persistir) ultrapassar esses 5s, o cliente aborta a conexão, o que
        // cancelaria SaveChangesAsync mesmo já tendo enviado a resposta ao
        // canal (ex. Telegram), perdendo a persistência sem perder o envio.
        // Ver incidente: resposta chegou no Telegram, mas a Message/PendingDispatch
        // nunca saiu de "Dispatching" no inbox.
        var pendingDispatch = await dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.TaskId == task.Id, cancellationToken);

        if (pendingDispatch is null || token is null || pendingDispatch.ExpectedToken != token)
        {
            // Sem distinguir "task desconhecida" de "token divergente" na
            // resposta — qualquer chamador externo que tente postar um
            // "task completed" falso recebe o mesmo 401 (design.md,
            // Decisão 6).
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
            "Round-trip A2A concluído para a task {TaskId}, sessão {SessionId}, estado {State}",
            task.Id,
            pendingDispatch.SessionId,
            task.Status.State);

        // Fecha o Non-Goal de entrega de resposta deixado em aberto em
        // inbox-orquestrador-debounce (design.md, Decision 3): quando a
        // task concluída carrega uma mensagem de resposta, entrega ao
        // canal de origem antes de remover o PendingDispatch. Sem
        // mensagem associada, não há o que entregar — comportamento
        // idêntico ao anterior (loga e remove).
        var responseText = ExtractResponseText(task);
        if (responseText is not null)
        {
            await DeliverResponseAsync(dbContext, credentialCipher, adapterRegistry, logger, pendingDispatch, responseText, CancellationToken.None);
        }

        // Único caminho que chega até aqui é uma push notification válida —
        // Completed cobre com e sem resposta textual associada
        // (inbox-mensagens-persistidas, design.md, Decisão 6).
        await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Completed, CancellationToken.None);

        dbContext.Remove(pendingDispatch);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return TypedResults.Ok();
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

    private static async Task DeliverResponseAsync(
        AppDbContext dbContext,
        IChannelCredentialCipher credentialCipher,
        IChannelAdapterRegistry adapterRegistry,
        ILogger logger,
        PendingDispatch pendingDispatch,
        string responseText,
        CancellationToken cancellationToken)
    {
        // Mesmo join de DebounceSweepService.TryDispatchAsync
        // (Session.ContactId → Contact.ChannelId → Channel), selecionando
        // os campos do Channel/Contact necessários para a entrega em vez
        // de AgentId.
        var dispatchInfo = await (
            from session in dbContext.Sessions
            join contact in dbContext.Contacts on session.ContactId equals contact.Id
            join channel in dbContext.Channels on contact.ChannelId equals channel.Id
            where session.Id == pendingDispatch.SessionId
            select new { channel.Id, channel.ChannelType, channel.EncryptedCredentials, contact.ExternalId }
        ).FirstAsync(cancellationToken);

        // O endpoint decifra, não o sender — simétrico a como o validador
        // recebe texto plano antes de cifrar na entrada (design.md,
        // Decision 3). IOutboundMessageSender não depende de
        // IChannelCredentialCipher.
        var outboundMessage = new OutboundMessage(
            dispatchInfo.Id,
            credentialCipher.Decrypt(dispatchInfo.EncryptedCredentials),
            dispatchInfo.ExternalId,
            responseText);

        var occurredAt = DateTimeOffset.UtcNow;

        try
        {
            var sender = adapterRegistry.GetOutboundMessageSender(dispatchInfo.ChannelType);
            await sender.SendAsync(outboundMessage, cancellationToken);
            dbContext.Messages.Add(MessageEntity.CreateOutbound(
                pendingDispatch.SessionId, responseText, occurredAt, MessageDeliveryStatus.Sent, deliveryFailureReason: null));
        }
        catch (Exception exception)
        {
            // Falha do sender é só logada nesta fatia — sem retry, sem
            // nova reapresentação do PendingDispatch, que já será
            // removido (design.md, Decision 3, Risks). A partir de
            // inbox-mensagens-persistidas, também vira estado persistido em
            // Message, não só log (design.md, Decisão 4).
            logger.LogError(
                exception,
                "Falha ao entregar a resposta do agente ao canal {ChannelId} (tipo {ChannelType})",
                dispatchInfo.Id,
                dispatchInfo.ChannelType);
            dbContext.Messages.Add(MessageEntity.CreateOutbound(
                pendingDispatch.SessionId, responseText, occurredAt, MessageDeliveryStatus.Failed, exception.Message));
        }
    }

    private static async Task UpdateMessageDispatchStatusesAsync(
        AppDbContext dbContext,
        Guid pendingDispatchId,
        MessageDispatchStatus status,
        CancellationToken cancellationToken)
    {
        var messages = await dbContext.Messages
            .Where(message => message.PendingDispatchId == pendingDispatchId)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.UpdateDispatchStatus(status);
        }
    }
}
