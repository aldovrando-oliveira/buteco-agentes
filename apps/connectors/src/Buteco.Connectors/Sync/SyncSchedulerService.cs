namespace Buteco.Connectors.Sync;

/// <summary>
/// O ciclo periódico (design.md da change ciclo-de-sincronizacao, D3): uma rodada logo
/// depois do boot e uma a cada <see cref="Interval"/>. A próxima só começa quando a
/// anterior termina, e o <see cref="PeriodicTimer"/> não acumula ticks perdidos: uma
/// rodada longa é seguida de UMA rodada, e não de várias. Registrado só com
/// <c>Api:BaseUrl</c> configurada (D2).
/// </summary>
public sealed class SyncSchedulerService(ISyncRound round, TimeProvider timeProvider, ILogger<SyncSchedulerService> logger) : BackgroundService
{
    /// <summary>Fixo (convenção 2): o da etapa 0. Gatilho para configurar: alguém precisar de outro valor.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            // A rodada já isola cada base; isto pega o que escapar dela, para o
            // agendamento não morrer na primeira falha (convenção 4).
            try
            {
                await round.RunAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Rodada de sincronização interrompida por {ExceptionType}.", exception.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
