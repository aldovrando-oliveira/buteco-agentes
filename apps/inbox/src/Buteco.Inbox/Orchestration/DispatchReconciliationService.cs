using A2A;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Orchestration;

/// <summary>
/// Varre as PendingDispatch em Dispatching e resolve as que nenhum push vai
/// resolver (#47; design.md da change pending-dispatch-orfa). Antes dela, o único
/// leitor de linha em Dispatching era o endpoint de push: se o push falhava,
/// chegava cedo demais ou nunca era enviado, a conversa ficava sem resposta e sem
/// aviso para sempre.
/// </summary>
/// <remarks>
/// <para>
/// <b>Duas regras, uma por tipo de linha.</b> Com TaskId, quem decide é o estado
/// da task em apps/api, consultado pelo GetTask do protocolo; a idade da linha
/// não entra, porque uma linha em Dispatching é legítima enquanto a task roda, por
/// quanto tempo for (D3). Sem TaskId não há a quem perguntar, e a linha é
/// encerrada como perda depois de um limite derivado (D7).
/// </para>
/// <para>
/// <b>Serviço próprio, e não um passo do <see cref="DebounceSweepService"/></b>
/// (D2): a cadência é outra (1 min contra 2 s) e cada linha faz uma chamada HTTP a
/// apps/api; uma apps/api lenta não pode atrasar o disparo de mensagens novas.
/// </para>
/// <para>
/// <b>Na parada</b> (D11): nenhuma linha é reivindicada depois de
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/>, e o trabalho em voo
/// segue até <see cref="DebounceSweepService.InFlightWorkDeadline"/> contado do
/// pedido de parada — em paralelo com a unidade do debounce, não somado a ela.
/// </para>
/// </remarks>
public sealed class DispatchReconciliationService(
    IServiceScopeFactory scopeFactory,
    IA2AClientFactory a2AClientFactory,
    IHostApplicationLifetime applicationLifetime,
    IOptions<DispatchReconciliationOptions> options,
    ILogger<DispatchReconciliationService> logger) : BackgroundService
{
    // Primeira vez que esta instância viu terminal uma task SEM carimbo terminal
    // (D3). A carência conta daqui: nunca na primeira observação (seria dobro com
    // um push em voo), nunca "jamais" (seria órfã). Em memória de propósito — um
    // reinício só recomeça a espera. Descartado a cada ciclo para as linhas que
    // saíram de Dispatching.
    private readonly Dictionary<string, DateTimeOffset> _firstSeenTerminalWithoutTimestamp = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        // Emitido sempre, no boot, com os três valores em vigor — mesma razão da
        // varredura de tasks não-terminais de apps/workers: silêncio tem de ser
        // distinguível de varredura ausente.
        logger.LogInformation(
            "Reconciliação de disparos em Dispatching ativa: intervalo de {Interval}, carência de {TerminalGrace} após o estado terminal, limite de {UntrackedDispatchMaxAge} para disparo sem TaskId, posse de {ClaimLease} por reivindicação.",
            settings.Interval,
            settings.TerminalGrace,
            settings.UntrackedDispatchMaxAge,
            settings.ClaimLease);

        // O prazo do trabalho em voo começa no PEDIDO de parada, não na vez deste
        // serviço parar: o Kestrel para antes e pode levar segundos (D11).
        using var inFlightDeadline = new CancellationTokenSource();
        using var onStopping = applicationLifetime.ApplicationStopping.Register(
            () => inFlightDeadline.CancelAfter(DebounceSweepService.InFlightWorkDeadline));

        using var timer = new PeriodicTimer(settings.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileAsync(inFlightDeadline.Token);
        }
    }

    private async Task ReconcileAsync(CancellationToken workToken)
    {
        List<Candidate> candidates;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            candidates = (await (
                from dispatch in dbContext.PendingDispatches
                join session in dbContext.Sessions on dispatch.SessionId equals session.Id
                join contact in dbContext.Contacts on session.ContactId equals contact.Id
                join channel in dbContext.Channels on contact.ChannelId equals channel.Id
                where dispatch.Status == PendingDispatchStatus.Dispatching
                select new { dispatch.Id, dispatch.SessionId, dispatch.TaskId, dispatch.LastMessageAt, dispatch.ReconciliationClaimedAt, channel.AgentId }
            ).ToListAsync(workToken))
                // Linha em posse de outra reivindicação não é nem consultada:
                // nada a fazer com ela até o prazo de posse vencer (D4).
                .Where(row => !IsHeld(row.ReconciliationClaimedAt))
                .Select(row => new Candidate(row.Id, row.SessionId, row.TaskId, row.LastMessageAt, row.AgentId))
                .ToList();
        }
        catch (Exception ex) when (!IsStopDeadline(ex, workToken))
        {
            // Mesma forma do DebounceSweepService (convenção 4): a consulta que
            // mais realisticamente falha está DENTRO do try, e o ciclo seguinte
            // consulta de novo, do zero.
            logger.LogError(ex, "Falha ao consultar disparos em Dispatching para reconciliação");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var activeTaskIds = candidates.Where(c => c.TaskId is not null).Select(c => c.TaskId!).ToHashSet();
        foreach (var stale in _firstSeenTerminalWithoutTimestamp.Keys.Where(taskId => !activeTaskIds.Contains(taskId)).ToList())
        {
            _firstSeenTerminalWithoutTimestamp.Remove(stale);
        }

        foreach (var candidate in candidates)
        {
            // Nenhuma reivindicação depois do pedido de parada (D11): o
            // stoppingToken deste serviço só é cancelado na vez dele, depois do
            // Kestrel.
            if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (candidate.TaskId is null)
                {
                    await CloseUntrackedIfExpiredAsync(candidate, workToken);
                }
                else
                {
                    await ReconcileTrackedAsync(candidate, candidate.TaskId, workToken);
                }
            }
            catch (Exception ex) when (!IsStopDeadline(ex, workToken))
            {
                // Uma linha que falha não impede as seguintes — mesmo nível de
                // captura, por unidade de trabalho, do DebounceSweepService.
                logger.LogError(ex, "Falha ao reconciliar o disparo da sessão {SessionId}", candidate.SessionId);
            }
            catch (OperationCanceledException)
            {
                // Prazo da parada vencido no meio da linha: ela continua
                // reivindicada em Dispatching e volta no boot seguinte (D11).
                return;
            }
        }
    }

    private async Task ReconcileTrackedAsync(Candidate candidate, string taskId, CancellationToken workToken)
    {
        AgentTask task;
        try
        {
            task = await a2AClientFactory.CreateForAgent(candidate.AgentId)
                .GetTaskAsync(new GetTaskRequest { Id = taskId }, workToken);
        }
        catch (Exception ex) when (!IsStopDeadline(ex, workToken))
        {
            // TaskNotFound, apps/api fora do ar ou timeout do cliente: a linha fica
            // como está e é consultada de novo no ciclo seguinte. Com o agente
            // apagado, isto se repete indefinidamente — gatilho registrado junto da
            // #77 (design.md, Risks).
            logger.LogWarning(
                ex,
                "Não foi possível consultar a task {TaskId} do disparo da sessão {SessionId} em apps/api; o disparo segue em Dispatching.",
                taskId,
                candidate.SessionId);
            return;
        }

        if (!task.Status.State.IsTerminal())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset terminalAt;
        if (task.Status.Timestamp is { } timestamp)
        {
            terminalAt = timestamp;
        }
        else if (!_firstSeenTerminalWithoutTimestamp.TryGetValue(taskId, out terminalAt))
        {
            _firstSeenTerminalWithoutTimestamp[taskId] = now;
            return;
        }

        if (now - terminalAt < options.Value.TerminalGrace)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingDispatch = await ClaimAsync(
            dbContext,
            dispatch => dispatch.Id == candidate.Id && dispatch.Status == PendingDispatchStatus.Dispatching && dispatch.TaskId == taskId,
            workToken);
        if (pendingDispatch is null)
        {
            return;
        }

        logger.LogInformation(
            "Disparo da sessão {SessionId} reconciliado pelo estado {State} da task {TaskId}, sem push notification recebida.",
            candidate.SessionId,
            task.Status.State,
            taskId);

        await scope.ServiceProvider.GetRequiredService<DispatchOutcomeProcessor>()
            .CompleteFromTaskAsync(pendingDispatch, task, workToken);
        _firstSeenTerminalWithoutTimestamp.Remove(taskId);
    }

    private async Task CloseUntrackedIfExpiredAsync(Candidate candidate, CancellationToken workToken)
    {
        if (DateTimeOffset.UtcNow - candidate.LastMessageAt < options.Value.UntrackedDispatchMaxAge)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingDispatch = await ClaimAsync(
            dbContext,
            dispatch => dispatch.Id == candidate.Id && dispatch.Status == PendingDispatchStatus.Dispatching && dispatch.TaskId == null,
            workToken);
        if (pendingDispatch is null)
        {
            return;
        }

        // Sem redisparo (D7): se o SendMessage tinha chegado a apps/api, o agente
        // responderia duas vezes. O resíduo é o oposto — o agente respondeu, o
        // push recebeu 401, e o contato recebe o aviso de falha (design.md, Risks).
        logger.LogError(
            "Mensagem de usuário perdida: disparo da sessão {SessionId} em Dispatching sem TaskId há mais de {UntrackedDispatchMaxAge}.",
            candidate.SessionId,
            options.Value.UntrackedDispatchMaxAge);

        await scope.ServiceProvider.GetRequiredService<DispatchOutcomeProcessor>()
            .FailAsync(pendingDispatch, workToken);
    }

    // Reivindicação pela troca do token, sob xmin (D4): a instância que perde
    // recebe DbUpdateConcurrencyException e deixa a linha para a vencedora.
    private async Task<PendingDispatch?> ClaimAsync(
        AppDbContext dbContext,
        System.Linq.Expressions.Expression<Func<PendingDispatch, bool>> stillOrphan,
        CancellationToken workToken)
    {
        var pendingDispatch = await dbContext.PendingDispatches.FirstOrDefaultAsync(stillOrphan, workToken);

        // Relido aqui, e não só na lista de candidatos: entre a lista e este
        // ponto há o GetTask, e outra instância pode ter reivindicado.
        if (pendingDispatch is null || IsHeld(pendingDispatch.ReconciliationClaimedAt))
        {
            return null;
        }

        pendingDispatch.ClaimForReconciliation(DateTimeOffset.UtcNow);
        try
        {
            await dbContext.SaveChangesAsync(workToken);
            return pendingDispatch;
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogDebug(
                "Disparo da sessão {SessionId} reivindicado por outro caminho antes desta reconciliação.",
                pendingDispatch.SessionId);
            return null;
        }
    }

    private bool IsHeld(DateTimeOffset? claimedAt) =>
        claimedAt is { } at && DateTimeOffset.UtcNow - at < options.Value.ClaimLease;

    private static bool IsStopDeadline(Exception exception, CancellationToken workToken) =>
        exception is OperationCanceledException && workToken.IsCancellationRequested;

    private sealed record Candidate(Guid Id, Guid SessionId, string? TaskId, DateTimeOffset LastMessageAt, Guid AgentId);
}
