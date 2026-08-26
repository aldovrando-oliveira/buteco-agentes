using A2A;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Options;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Orchestration;

// Único componente orientado a timer/scheduling do projeto (design.md,
// Context) — varre pending_dispatches periodicamente, não usa Timer por
// conversa (design.md, Decisão 2), porque o buffer persistido precisa
// funcionar igual entre múltiplas instâncias (Decisão 1).
public sealed class DebounceSweepService(
    IServiceScopeFactory scopeFactory,
    IA2AClientFactory a2AClientFactory,
    IOptions<DebounceOptions> debounceOptions,
    IOptions<PublicUrlOptions> publicUrlOptions,
    ILogger<DebounceSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(debounceOptions.Value.SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessDueDispatchesAsync(stoppingToken);
        }
    }

    private async Task ProcessDueDispatchesAsync(CancellationToken cancellationToken)
    {
        List<Guid> candidateIds;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTimeOffset.UtcNow - debounceOptions.Value.Window;

            candidateIds = await dbContext.PendingDispatches
                .Where(dispatch => dispatch.Status == PendingDispatchStatus.Pending && dispatch.LastMessageAt <= cutoff)
                .Select(dispatch => dispatch.Id)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Falha de infraestrutura na própria consulta (ex.: queda de
            // conexão do Postgres) — loga e encerra este ciclo sem
            // processar nenhum candidato; o próximo tick do PeriodicTimer
            // consulta de novo, do zero (design.md de
            // inbox-sweep-service-resiliencia, Decisão 3). Sem este catch,
            // a exceção escaparia de ExecuteAsync e o
            // BackgroundServiceExceptionBehavior padrão (StopHost)
            // derrubaria o processo inteiro — reproduzido de verdade
            // (Npgsql 57P01) sob contenção de recursos.
            logger.LogError(ex, "Falha ao consultar candidatos elegíveis para disparo de debounce");
            return;
        }

        foreach (var candidateId in candidateIds)
        {
            try
            {
                await TryDispatchAsync(candidateId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Falha ao processar um candidato específico não pode
                // bloquear os demais candidatos deste ciclo — mesmo nível
                // de captura (por unidade de trabalho, não por lote) do
                // catch por mensagem de TaskJobConsumer (apps/workers).
                // Loga e segue para o próximo candidato do foreach.
                logger.LogError(ex, "Falha ao processar disparo do candidato de debounce {PendingDispatchId}", candidateId);
            }
        }
    }

    private async Task TryDispatchAsync(Guid pendingDispatchId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingDispatch = await dbContext.PendingDispatches
            .FirstOrDefaultAsync(
                dispatch => dispatch.Id == pendingDispatchId && dispatch.Status == PendingDispatchStatus.Pending,
                cancellationToken);

        if (pendingDispatch is null)
        {
            // Já reivindicada, disparada ou removida por outra instância
            // entre a varredura e este momento.
            return;
        }

        var token = Guid.NewGuid().ToString("N");
        pendingDispatch.MarkDispatching(token);
        await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Dispatching, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Prova de idempotência (design.md, Decisão 5): outra
            // instância reivindicou esta linha entre a leitura e este
            // claim — não é um erro, é o caminho esperado da concorrência.
            return;
        }

        var dispatchInfo = await (
            from session in dbContext.Sessions
            join contact in dbContext.Contacts on session.ContactId equals contact.Id
            join channel in dbContext.Channels on contact.ChannelId equals channel.Id
            where session.Id == pendingDispatch.SessionId
            select new { session.ContextId, channel.AgentId }
        ).FirstAsync(cancellationToken);

        var client = a2AClientFactory.CreateForAgent(dispatchInfo.AgentId);
        var request = BuildSendMessageRequest(pendingDispatch, dispatchInfo.ContextId, token);

        SendMessageResponse response;
        try
        {
            response = await client.SendMessageAsync(request, cancellationToken);
        }
        catch (A2AException exception)
        {
            // Erro de protocolo (ex. agente desconhecido) — não é
            // transitório, reintentar não mudaria o resultado. Encerra
            // como a rejeição síncrona da Decisão 8, não como falha de
            // transporte da Decisão 9.
            logger.LogWarning(
                exception,
                "SendMessage rejeitado no nível de protocolo A2A para a sessão {SessionId}",
                pendingDispatch.SessionId);
            dbContext.Remove(pendingDispatch);
            await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Failed, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            await HandleTransportFailureAsync(dbContext, pendingDispatch, exception, cancellationToken);
            return;
        }

        await HandleResponseAsync(dbContext, pendingDispatch, response, cancellationToken);
    }

    private SendMessageRequest BuildSendMessageRequest(PendingDispatch pendingDispatch, string contextId, string token)
    {
        var pushNotificationBaseUrl = publicUrlOptions.Value.BaseUrl.TrimEnd('/');

        return new SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = Role.User,
                Parts = [Part.FromText(pendingDispatch.ConcatenatedText())],
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
            },
            Configuration = new SendMessageConfiguration
            {
                PushNotificationConfig = new PushNotificationConfig
                {
                    Url = $"{pushNotificationBaseUrl}{PushNotificationEndpoints.RoutePattern}",
                    Token = token,
                },
            },
        };
    }

    private async Task HandleResponseAsync(
        AppDbContext dbContext,
        PendingDispatch pendingDispatch,
        SendMessageResponse response,
        CancellationToken cancellationToken)
    {
        if (response.Task is null || response.Task.Status.State != TaskState.Submitted)
        {
            // Resposta síncrona já terminal (ex. Rejected — agente
            // inativo, sem provider configurado) ou payload inesperado
            // (Message em vez de Task, fora do fluxo desta fatia): a spec
            // a2a-push-notifications garante que uma task rejeitada nunca
            // dispara o webhook, então não há por que esperar por ele
            // (design.md, Decisão 8).
            logger.LogInformation(
                "SendMessage para a sessão {SessionId} encerrou de forma síncrona com estado {State}",
                pendingDispatch.SessionId,
                response.Task?.Status.State);
            dbContext.Remove(pendingDispatch);
            await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Failed, cancellationToken);
        }
        else
        {
            pendingDispatch.RegisterTaskId(response.Task.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleTransportFailureAsync(
        AppDbContext dbContext,
        PendingDispatch pendingDispatch,
        Exception exception,
        CancellationToken cancellationToken)
    {
        pendingDispatch.RegisterTransportFailure(DateTimeOffset.UtcNow);

        if (pendingDispatch.AttemptCount >= debounceOptions.Value.MaxDispatchAttempts)
        {
            logger.LogError(
                exception,
                "Mensagem de usuário perdida após {AttemptCount} tentativas de SendMessage para a sessão {SessionId}",
                pendingDispatch.AttemptCount,
                pendingDispatch.SessionId);
            pendingDispatch.MarkFailed();
            dbContext.Remove(pendingDispatch);
            await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Failed, cancellationToken);
        }
        else
        {
            logger.LogWarning(
                exception,
                "Falha de transporte ao disparar SendMessage para a sessão {SessionId} (tentativa {AttemptCount}/{MaxAttempts}) — reintentará na próxima varredura",
                pendingDispatch.SessionId,
                pendingDispatch.AttemptCount,
                debounceOptions.Value.MaxDispatchAttempts);
            // RegisterTransportFailure devolveu PendingDispatch a Pending —
            // mesmo espelhamento em Message (design.md, Decisão 6).
            await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Pending, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            // Timeout do HttpClient, não cancelamento explícito do
            // orquestrador — mesmo padrão de AgentReferenceValidator.
            TaskCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
}
