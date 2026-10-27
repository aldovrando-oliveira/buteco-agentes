namespace Buteco.Api.Identity.Storage;

/// <summary>
/// Roda a limpeza das entradas de token a cada <see cref="IdentityTokenPruneSchedule.Interval"/>
/// (design.md da change limpeza-tokens-openiddict, D4), no idioma do
/// <c>IdentityKeyRotationService</c>:
/// <list type="bullet">
/// <item><b>o primeiro ciclo é no primeiro tique</b>, um intervalo depois do start, e não
/// no start: o boot e as fixtures continuam sem I/O, e nada que importe se acumula numa
/// hora;</item>
/// <item><c>try/catch</c> por ciclo (convenção 4): falha de banco, ou a
/// <see cref="AggregateException"/> que o <c>PruneAsync</c> lança quando um lote falha,
/// vira log, e o próximo tique tenta de novo. O serviço só para pelo cancelamento.</item>
/// </list>
/// </summary>
public sealed class IdentityTokenPruneService(
    IIdentityTokenPruner pruner,
    IdentityTokenPruneSchedule schedule,
    TimeProvider timeProvider,
    ILogger<IdentityTokenPruneService> logger) : BackgroundService
{
    public static readonly EventId PruneCycleFailedEvent = new(1185, "IdentityTokenPruneCycleFailed");
    public static readonly EventId PrunedEvent = new(1186, "IdentityTokenPruned");

    // Criado na construção, como na rotação: o relógio começa quando o host sobe, e o
    // teste com FakeTimeProvider fica determinístico.
    private readonly PeriodicTimer _timer = new(schedule.Interval, timeProvider);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result = await pruner.RunCycleAsync(stoppingToken);

                if (result.WasSkipped)
                {
                    logger.LogDebug("Limpeza das entradas de token pulada: outra instância está com o ciclo.");
                }
                else if (result.Tokens + result.Authorizations > 0)
                {
                    logger.LogInformation(
                        PrunedEvent,
                        "Limpeza das entradas de token removeu {Tokens} tokens e {Authorizations} autorizações.",
                        result.Tokens,
                        result.Authorizations);
                }
                else
                {
                    logger.LogDebug("Limpeza das entradas de token: nada a remover.");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    PruneCycleFailedEvent,
                    exception,
                    "Limpeza das entradas de token falhou neste ciclo; o próximo tenta de novo.");
            }
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// O intervalo do serviço. Constante, e não configuração (convenção 2); singleton trocável
/// por DI porque os testes mudam o intervalo, como o <c>IdentityKeyRotationSchedule</c>.
/// </summary>
public sealed class IdentityTokenPruneSchedule
{
    public TimeSpan Interval { get; init; } = IdentityTokenPrunePolicy.Interval;
}
