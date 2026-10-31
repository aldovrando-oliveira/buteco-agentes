using Buteco.Api.Identity.Clients;
using Buteco.Api.Users.Bootstrap;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Aquecimento e rotação das chaves do emissor (design.md da change emissor-oidc-api, D6
/// e D7), em duas fases:
/// <list type="number">
/// <item><b>aquecimento:</b> logo depois do start, sem bloquear o boot (no .NET 10 o
/// <c>ExecuteAsync</c> do <c>BackgroundService</c> roda fora do <c>StartAsync</c>), em passos
/// independentes (design.md da change usuarios-locais-login-emissor, D3.13): carregar o
/// anel, sincronizar o catálogo de clientes e rodar o bootstrap do primeiro Admin. Cada passo tem o próprio <c>try/catch</c> e
/// <see cref="EventId"/>: a falha de um não impede os outros, o que falhou tenta de novo a
/// cada <see cref="IdentityKeyRotationSchedule.WarmupRetryInterval"/>, e os concluídos
/// não repetem;</item>
/// <item><b>rotação:</b> a cada <see cref="IdentityKeyRotationSchedule.CheckInterval"/>,
/// <see cref="IdentityKeyRing.RefreshAsync"/> cria a próxima chave quando é hora e
/// recarrega o anel; no mesmo tique, num <c>try/catch</c> separado, o catálogo sincroniza
/// de novo e corrige divergência.</item>
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
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<IdentityKeyRotationService> logger) : BackgroundService
{
    public static readonly EventId WarmupFailedEvent = new(1183, "IdentityKeyWarmupFailed");
    public static readonly EventId RotationCycleFailedEvent = new(1184, "IdentityKeyRotationCycleFailed");
    public static readonly EventId BootstrapFailedEvent = new(1187, "UserBootstrapWarmupFailed");
    public static readonly EventId CatalogFailedEvent = new(1193, "IdentityClientCatalogFailed");

    // Criado na construção, e não depois do aquecimento: o relógio da verificação começa
    // quando o host sobe, e um tique vencido durante o aquecimento fica pendente e roda
    // logo depois dele. É o que deixa o teste de rotação determinístico com
    // FakeTimeProvider, sem depender de em que fase o serviço estava ao avançar o relógio.
    private readonly PeriodicTimer _timer = new(schedule.CheckInterval, timeProvider);
    private readonly TimeSpan _warmupRetryInterval = schedule.WarmupRetryInterval;
    private readonly TimeProvider _timeProvider = timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<WarmupStep>
        {
            new("as chaves do emissor", WarmupFailedEvent, ring.EnsureLoadedAsync),
            new("o catálogo de clientes", CatalogFailedEvent, SyncCatalogAsync),
            new("o bootstrap do primeiro Admin", BootstrapFailedEvent, RunBootstrapAsync),
        };

        while (pending.Count > 0)
        {
            foreach (var step in pending.ToList())
            {
                try
                {
                    await step.RunAsync(stoppingToken);
                    pending.Remove(step);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(
                        step.FailedEvent,
                        exception,
                        "Aquecimento falhou para {Step}; nova tentativa em {Interval}.",
                        step.Name,
                        _warmupRetryInterval);
                }
            }

            if (pending.Count > 0)
            {
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

            try
            {
                await SyncCatalogAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    CatalogFailedEvent,
                    exception,
                    "Sincronização do catálogo de clientes falhou neste ciclo; o próximo tenta de novo.");
            }
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }

    private async Task SyncCatalogAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IIdentityClientCatalog>().SyncAsync(cancellationToken);
    }

    private async Task RunBootstrapAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IUserBootstrapper>().RunAsync(cancellationToken);
    }

    private sealed record WarmupStep(string Name, EventId FailedEvent, Func<CancellationToken, Task> RunAsync);
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
