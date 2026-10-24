namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Aquecimento e rotação das chaves do emissor (design.md da change emissor-oidc-api, D6
/// e D7), em duas fases:
/// <list type="number">
/// <item><b>aquecimento:</b> carrega o anel logo depois do start, sem bloquear o boot (no
/// .NET 10 o <c>ExecuteAsync</c> do <c>BackgroundService</c> roda fora do <c>StartAsync</c>),
/// e repete a cada <see cref="IdentityKeyRotationSchedule.WarmupRetryInterval"/> até
/// carregar. Cada tentativa é independente: falha só gera log;</item>
/// <item><b>rotação:</b> a cada <see cref="IdentityKeyRotationSchedule.CheckInterval"/>,
/// <see cref="IdentityKeyRing.RefreshAsync"/> cria a próxima chave quando é hora e
/// recarrega o anel.</item>
/// </list>
/// O gate continua sendo a garantia: o aquecimento só faz o primeiro <c>/health</c>
/// raramente encontrar o anel vazio.
///
/// <para>
/// Mesma forma do <c>KnowledgeIndexingRequestSweepService</c>: <c>try/catch</c> por ciclo
/// envolvendo o banco (convenção 4), e o serviço nunca morre por falha recuperável.
/// </para>
/// </summary>
public sealed class IdentityKeyRotationService(
    IdentityKeyRing ring,
    IdentityKeyRotationSchedule schedule,
    TimeProvider timeProvider,
    ILogger<IdentityKeyRotationService> logger) : BackgroundService
{
    public static readonly EventId WarmupFailedEvent = new(1183, "IdentityKeyWarmupFailed");
    public static readonly EventId RotationCycleFailedEvent = new(1184, "IdentityKeyRotationCycleFailed");

    // Criado na construção, e não depois do aquecimento: o relógio da verificação começa
    // quando o host sobe, e um tique vencido durante o aquecimento fica pendente e roda
    // logo depois dele. É o que deixa o teste de rotação determinístico com
    // FakeTimeProvider, sem depender de em que fase o serviço estava ao avançar o relógio.
    private readonly PeriodicTimer _timer = new(schedule.CheckInterval, timeProvider);
    private readonly TimeSpan _warmupRetryInterval = schedule.WarmupRetryInterval;
    private readonly TimeProvider _timeProvider = timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!ring.IsLoaded)
        {
            try
            {
                await ring.EnsureLoadedAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    WarmupFailedEvent,
                    exception,
                    "Aquecimento das chaves do emissor falhou; nova tentativa em {Interval}.",
                    _warmupRetryInterval);

                await Task.Delay(_warmupRetryInterval, _timeProvider, stoppingToken);
            }
        }

        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ring.RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    RotationCycleFailedEvent,
                    exception,
                    "Verificação de rotação das chaves do emissor falhou neste ciclo; o próximo tenta de novo.");
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
/// Os dois intervalos do serviço. Constantes, e não configuração (convenção 2); é
/// singleton trocável por DI porque os testes encurtam os dois, como o
/// <c>KnowledgeIndexingRequestSchedule</c>.
/// </summary>
public sealed class IdentityKeyRotationSchedule
{
    public TimeSpan WarmupRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan CheckInterval { get; init; } = IdentityKeyRotationPolicy.CheckInterval;
}
