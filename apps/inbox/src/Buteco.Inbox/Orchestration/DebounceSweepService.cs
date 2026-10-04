using System.Globalization;
using System.Text.Json;
using A2A;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Options;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Orchestration;

// Primeiro componente orientado a timer/scheduling do projeto (design.md,
// Context) — hoje não é o único: NonTerminalTaskDetectorService (apps/workers)
// e DispatchReconciliationService (#47) têm a mesma forma. Varre
// pending_dispatches periodicamente, não usa Timer por conversa (design.md,
// Decisão 2), porque o buffer persistido precisa funcionar igual entre
// múltiplas instâncias (Decisão 1).
public sealed class DebounceSweepService(
    IServiceScopeFactory scopeFactory,
    IA2AClientFactory a2AClientFactory,
    IHostApplicationLifetime applicationLifetime,
    IOptions<DebounceOptions> debounceOptions,
    IOptions<PublicUrlOptions> publicUrlOptions,
    ILogger<DebounceSweepService> logger) : BackgroundService
{
    /// <summary>
    /// Prazo do trabalho que já estava em voo quando a parada foi pedida — o
    /// trecho entre a reivindicação e a gravação do TaskId aqui, e a entrega em
    /// voo em <see cref="DispatchReconciliationService"/> (design.md da change
    /// pending-dispatch-orfa, D11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A derivação:</b> 5 s do timeout do cliente A2A (Program.cs, no
    /// <c>AddHttpClient(A2AClientFactory.HttpClientName)</c>) mais folga para as
    /// gravações, abaixo dos 10 s padrão do <c>stop_grace_period</c> do Compose
    /// (o serviço <c>inbox</c> do <c>docker-compose.prod.yml</c> não o define). O
    /// <c>ShutdownTimeout</c> do host (30 s) não é o limite que vale: o
    /// <c>SIGKILL</c> do Compose chega antes.
    /// </para>
    /// <para>
    /// <b>Medido em 04/10/2026</b> (<c>InboxShutdownUnderKestrelTests</c>): o
    /// trabalho em voo corre em PARALELO com a parada dos outros serviços; só
    /// as esperas são em série. O que estourava os 10 s era reivindicar trabalho
    /// novo depois do pedido de parada (10,9 s), e é por isso que
    /// <see cref="IHostApplicationLifetime.ApplicationStopping"/> é conferido
    /// antes de cada reivindicação.
    /// </para>
    /// <para>
    /// <b>Gatilho de recalibração:</b> mudança no timeout do cliente A2A, um
    /// <c>stop_grace_period</c> explícito no inbox, ou um <c>ShutdownTimeout</c>
    /// explícito.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan InFlightWorkDeadline = TimeSpan.FromSeconds(8);

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
            // ApplicationStopping, e não o stoppingToken: o host para os serviços
            // em série, e o Kestrel para ANTES deste serviço, esperando as
            // requisições em voo. Durante essa espera o stoppingToken daqui
            // ainda não foi cancelado, e uma janela de debounce que vencesse
            // abriria uma unidade nova de 8 s depois do pedido de parada —
            // medido: 10,9 s (design.md, D11). O candidato fica Pending para o
            // boot seguinte.
            if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }

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

        // Daqui até a gravação do TaskId, a unidade NÃO obedece à parada, só ao
        // próprio prazo (design.md da change pending-dispatch-orfa, D11). Antes,
        // a parada cancelava o SendMessage em voo; se ele já tinha chegado a
        // apps/api, a task rodava, o push chegava com TaskId desconhecido (401) e
        // a resposta se perdia — em todo deploy com mensagem em voo. Medido: a
        // parada passou de 6 ms (TaskId nulo) para ~1.009 ms (TaskId gravado) com
        // um SendMessage de 1 s.
        using var unit = new CancellationTokenSource(InFlightWorkDeadline);
        cancellationToken = unit.Token;

        var dispatchInfo = await (
            from session in dbContext.Sessions
            join contact in dbContext.Contacts on session.ContactId equals contact.Id
            join channel in dbContext.Channels on contact.ChannelId equals channel.Id
            where session.Id == pendingDispatch.SessionId
            select new { session.ContextId, channel.AgentId, channel.ChannelType, contact.ExternalId }
        ).FirstAsync(cancellationToken);

        var client = a2AClientFactory.CreateForAgent(dispatchInfo.AgentId);
        var request = BuildSendMessageRequest(
            pendingDispatch, dispatchInfo.ContextId, token, dispatchInfo.ChannelType, dispatchInfo.ExternalId);

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
            await GetOutcomeProcessor(scope).FailAsync(pendingDispatch, cancellationToken);
            return;
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            await HandleTransportFailureAsync(scope, dbContext, pendingDispatch, exception, cancellationToken);
            return;
        }

        await HandleResponseAsync(scope, dbContext, pendingDispatch, response, cancellationToken);
    }

    // Chave de Message.Metadata que carrega o instante de recebimento da
    // última mensagem do buffer — mesmo nome usado do lado da leitura em
    // apps/workers (design.md da change inbox-instante-mensagem, Decisão
    // D2). Duplicado deliberadamente em vez de extraído para libs/: os dois
    // apps são isolados sem ProjectReference cruzado, e o helper de
    // serialização é pequeno o suficiente para não justificar uma
    // biblioteca nova (convenção 2).
    private const string MessageInstantMetadataKey = "messageInstant";

    // Chaves novas de contexto de canal (design.md da change
    // inbox-contexto-canal, D3/D4) — mesmo raciocínio de
    // MessageInstantMetadataKey: nomes batendo com o lado da leitura em
    // apps/workers, duplicado deliberadamente (apps isolados sem
    // ProjectReference cruzado), chaves escalares separadas, nunca
    // agrupadas num objeto (D3).
    private const string ChannelTypeMetadataKey = "channelType";
    private const string ContactExternalIdMetadataKey = "contactExternalId";

    private SendMessageRequest BuildSendMessageRequest(
        PendingDispatch pendingDispatch, string contextId, string token, string channelType, string contactExternalId)
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
                Metadata = new Dictionary<string, JsonElement>
                {
                    [MessageInstantMetadataKey] = EncodeMessageInstant(pendingDispatch.LastMessageAt),
                    // channelType/contactExternalId são lidos direto de
                    // Channel/Contact, atribuídos pelo adapter/provedor do
                    // canal — nunca texto digitado pelo usuário final
                    // (design.md, Goals). Sem transformação: string crua
                    // (D4).
                    [ChannelTypeMetadataKey] = EncodeScalar(channelType),
                    [ContactExternalIdMetadataKey] = EncodeScalar(contactExternalId),
                },
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

    // Valor escalar — nunca um objeto, para eliminar por construção a
    // variante séria do mecanismo em que um JsonElement pré-materializado
    // sem A2AJsonUtilities.DefaultOptions sobrevive à re-serialização do
    // AgentTask e grava formato errado em disco (design.md, Decisão D2 de
    // inbox-instante-mensagem, reafirmada como D3 de inbox-contexto-canal;
    // mesma classe de defeito de PushNotificationConfigCodec/
    // ConversationSessionCodec em apps/workers).
    private static JsonElement EncodeMessageInstant(DateTimeOffset instant) =>
        EncodeScalar(instant.ToString("O", CultureInfo.InvariantCulture));

    private static JsonElement EncodeScalar(string value) =>
        JsonSerializer.SerializeToElement(value, A2AJsonUtilities.DefaultOptions);

    private async Task HandleResponseAsync(
        IServiceScope scope,
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
            await GetOutcomeProcessor(scope).FailAsync(pendingDispatch, cancellationToken);
            return;
        }

        pendingDispatch.RegisterTaskId(response.Task.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task HandleTransportFailureAsync(
        IServiceScope scope,
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
            await GetOutcomeProcessor(scope).FailAsync(pendingDispatch, cancellationToken);
            return;
        }

        logger.LogWarning(
            exception,
            "Falha de transporte ao disparar SendMessage para a sessão {SessionId} (tentativa {AttemptCount}/{MaxAttempts}) — reintentará na próxima varredura",
            pendingDispatch.SessionId,
            pendingDispatch.AttemptCount,
            debounceOptions.Value.MaxDispatchAttempts);
        // RegisterTransportFailure devolveu PendingDispatch a Pending —
        // mesmo espelhamento em Message (design.md, Decisão 6).
        await UpdateMessageDispatchStatusesAsync(dbContext, pendingDispatch.Id, MessageDispatchStatus.Pending, cancellationToken);
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

    // Resolvido só nos caminhos de falha, no escopo do candidato: o processador
    // compartilha o mesmo AppDbContext, que já rastreia a linha reivindicada.
    private static DispatchOutcomeProcessor GetOutcomeProcessor(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<DispatchOutcomeProcessor>();

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
