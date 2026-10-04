namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Reenvia os pedidos de indexação que ficaram para trás (design.md da change
/// indexacao-sem-job-orfao, D3): publicação que falhou no fim da escrita, escrita
/// que pulou o despacho pela janela de D8, processo que caiu antes de despachar, e
/// os órfãos que a migração recuperou (D6).
///
/// <para>
/// <b>PRIMEIRO <c>BackgroundService</c> DO <c>apps/api</c>.</b> Dele vem só a forma de
/// <c>DebounceSweepService</c> (<c>apps/inbox</c>) e de
/// <c>NonTerminalTaskDetectorService</c> (<c>apps/workers</c>): <c>PeriodicTimer</c>, e
/// <c>try/catch</c> por ciclo envolvendo a consulta inclusive (convenção 4) — a chamada
/// que mais realisticamente falha é a do banco, e ela fica dentro. Um ciclo que falha
/// loga e espera o próximo tique; o serviço nunca morre por uma falha recuperável.
/// </para>
/// </summary>
public sealed class KnowledgeIndexingRequestSweepService(
    KnowledgeIndexingRequestDispatcher dispatcher,
    KnowledgeIndexingRequestSchedule schedule,
    ILogger<KnowledgeIndexingRequestSweepService> logger) : BackgroundService
{
    /// <summary>Ciclo que falhou antes de publicar (consulta ao banco, por exemplo).</summary>
    public static readonly EventId SweepCycleFailedEvent = new(1034, "KnowledgeIndexingRequestSweepCycleFailed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(schedule.SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await dispatcher.SweepOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    SweepCycleFailedEvent,
                    exception,
                    "Varredura de pedidos de indexação falhou neste ciclo; o próximo tenta de novo.");
            }
        }
    }
}
