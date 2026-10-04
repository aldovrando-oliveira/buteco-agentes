using System.Diagnostics;

namespace Buteco.Connectors.Sync;

public interface ISyncRound
{
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Uma rodada sobre todas as bases <c>Synced</c>, inclusive inativas, uma por vez, na
/// ordem de <c>GET /sync/knowledge-bases</c> (design.md da change ciclo-de-sincronizacao,
/// D3), e o ciclo avulso do "Sincronizar agora" (D9). Os dois passam pelo mesmo lock por
/// base.
/// </summary>
public sealed class SyncRound(
    ISyncApiClient api,
    KnowledgeBaseSyncCycle cycle,
    SyncInProgress inProgress,
    RefusalMemory memory,
    ILogger<SyncRound> logger) : ISyncRound
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var bases = await api.ListKnowledgeBasesAsync(cancellationToken);
        if (bases.Status != SyncApiStatus.Ok)
        {
            logger.LogError("Rodada de sincronização sem a lista de bases: apps/api {Status}.", bases.Status);
            return;
        }

        var stopped = false;
        foreach (var knowledgeBase in bases.Value!)
        {
            var outcome = await RunOneAsync(knowledgeBase, cancellationToken);
            if (outcome == SyncCycleOutcome.StopRound)
            {
                stopped = true;
                break;
            }
        }

        // Só uma rodada completa sabe quais bases saíram da lista (D14).
        if (!stopped)
        {
            memory.KeepOnlyBases(bases.Value!.Select(knowledgeBase => knowledgeBase.Id).ToHashSet());
        }

        // A duração é o gatilho para rever o paralelismo (D3).
        logger.LogInformation(
            "Rodada de sincronização: {Count} bases em {ElapsedMs} ms{Stopped}.",
            bases.Value!.Count, stopwatch.ElapsedMilliseconds, stopped ? ", interrompida" : "");
    }

    /// <summary>
    /// "Sincronizar agora" (D9): dispara o ciclo da base numa tarefa própria, com o token
    /// de parada da aplicação, e devolve <c>false</c> se um ciclo dela já está em curso.
    /// </summary>
    public bool TryRunInBackground(SyncedKnowledgeBase knowledgeBase, CancellationToken stoppingToken)
    {
        if (!inProgress.TryStart(knowledgeBase.Id))
        {
            return false;
        }

        _ = Task.Run(() => RunStartedAsync(knowledgeBase, stoppingToken), CancellationToken.None);
        return true;
    }

    /// <summary>Uma base da rodada; <c>null</c> quando ela já está sincronizando e é pulada (D9).</summary>
    private async Task<SyncCycleOutcome?> RunOneAsync(SyncedKnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        if (!inProgress.TryStart(knowledgeBase.Id))
        {
            logger.LogInformation("Base {KnowledgeBaseId} já em sincronização: pulada nesta rodada.", knowledgeBase.Id);
            return null;
        }

        return await RunStartedAsync(knowledgeBase, cancellationToken);
    }

    /// <summary>
    /// Cada base no próprio <c>try/catch</c> (D8, convenção 4): uma exceção inesperada
    /// numa base é logada, e a rodada segue. O lock sai sempre.
    /// </summary>
    private async Task<SyncCycleOutcome> RunStartedAsync(SyncedKnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        try
        {
            return await cycle.RunAsync(knowledgeBase, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SyncCycleOutcome.StopRound;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Ciclo da base {KnowledgeBaseId} interrompido por {ExceptionType}; a rodada segue.",
                knowledgeBase.Id, exception.GetType().Name);
            return SyncCycleOutcome.Completed;
        }
        finally
        {
            inProgress.Finish(knowledgeBase.Id);
        }
    }
}
