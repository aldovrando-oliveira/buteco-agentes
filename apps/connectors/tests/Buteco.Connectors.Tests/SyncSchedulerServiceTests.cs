using System.Threading.Channels;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O agendamento (design.md da change ciclo-de-sincronizacao, D3), com o relógio manual:
/// nenhum teste espera tempo de verdade para provar ordem ou intervalo. A espera nos
/// sinais tem um limite só como proteção contra travar a suíte; o que decide cada
/// asserção é o relógio e a contagem de disparos do timer.
/// </summary>
public class SyncSchedulerServiceTests
{
    private static readonly TimeSpan SafetyLimit = TimeSpan.FromSeconds(30);

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeRound _round = new();

    private SyncSchedulerService Service() => new(_round, _clock, NullLogger<SyncSchedulerService>.Instance);

    [Fact]
    public void Interval_IsFiveMinutes() => Assert.Equal(TimeSpan.FromMinutes(5), SyncSchedulerService.Interval);

    [Fact]
    public async Task FirstRound_RunsRightAfterStart_WithoutWaitingTheInterval()
    {
        using var service = Service();

        await service.StartAsync(CancellationToken.None);
        var first = await _round.NextCallAsync(SafetyLimit);

        Assert.Equal(0, _clock.TimerFirings);
        first.Release();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NextRound_OnlyAfterTheInterval()
    {
        using var service = Service();
        await service.StartAsync(CancellationToken.None);
        (await _round.NextCallAsync(SafetyLimit)).Release();

        _clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _clock.TimerFirings);
        Assert.Equal(1, _round.Calls);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, _clock.TimerFirings);
        var second = await _round.NextCallAsync(SafetyLimit);

        Assert.Equal(2, _round.Calls);
        second.Release();
        await service.StopAsync(CancellationToken.None);
    }

    // A rodada longa: três intervalos passam com ela em curso, e quando ela termina vem
    // UMA rodada seguinte, nunca duas ao mesmo tempo.
    [Fact]
    public async Task LongRound_NeverOverlaps_AndTheMissedTicksDoNotStack()
    {
        using var service = Service();
        await service.StartAsync(CancellationToken.None);
        var first = await _round.NextCallAsync(SafetyLimit);

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(3, _clock.TimerFirings);
        Assert.Equal(1, _round.Calls);
        first.Release();
        var second = await _round.NextCallAsync(SafetyLimit);
        second.Release();

        Assert.Equal(1, _round.MaxActive);
        await service.StopAsync(CancellationToken.None);
    }

    // O que impede o acúmulo é o PeriodicTimer, e é ele, sobre o relógio manual, que se
    // mede aqui: três disparos sem ninguém esperando completam UMA espera, e a seguinte
    // fica pendente. Determinístico, sem espera real.
    [Fact]
    public void PeriodicTimer_CoalescesTheTicksMissedWhileARoundRuns()
    {
        using var timer = new PeriodicTimer(SyncSchedulerService.Interval, _clock);

        _clock.Advance(TimeSpan.FromMinutes(15));
        var first = timer.WaitForNextTickAsync();
        var second = timer.WaitForNextTickAsync();

        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public async Task FailingRound_DoesNotStopTheScheduler()
    {
        using var service = Service();
        await service.StartAsync(CancellationToken.None);
        (await _round.NextCallAsync(SafetyLimit)).Fail(new InvalidOperationException("rodada quebrada"));

        _clock.Advance(SyncSchedulerService.Interval);
        var second = await _round.NextCallAsync(SafetyLimit);

        second.Release();
        await service.StopAsync(CancellationToken.None);
    }

    // Composição: o agendamento só existe com o endereço do apps/api (D2), e a composição
    // padrão do factory, com o endereço vazio (tarefa 2.3), não o registra.
    [Fact]
    public async Task DefaultFactory_DoesNotRegisterTheScheduler()
    {
        await using var factory = new ConnectorsFactory();
        _ = factory.CreateClient();

        Assert.DoesNotContain(factory.CapturedServices!, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(SyncSchedulerService));
    }

    [Fact]
    public async Task FactoryWithApiBaseUrl_RegistersTheScheduler()
    {
        await using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = "http://api.test" });
        _ = factory.Services;

        Assert.Contains(factory.CapturedServices!, descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(SyncSchedulerService));
    }

    private sealed class FakeRound : ISyncRound
    {
        private readonly Channel<Call> _calls = Channel.CreateUnbounded<Call>();
        private int _active;
        private int _calls_;
        private int _maxActive;

        public int Calls => Volatile.Read(ref _calls_);

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls_);
            var active = Interlocked.Increment(ref _active);
            InterlockedMax(ref _maxActive, active);
            var call = new Call();
            await _calls.Writer.WriteAsync(call, cancellationToken);
            try
            {
                await call.Completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public async Task<Call> NextCallAsync(TimeSpan limit) => await _calls.Reader.ReadAsync().AsTask().WaitAsync(limit);

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    private sealed class Call
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Completion.TrySetResult();

        public void Fail(Exception exception) => Completion.TrySetException(exception);
    }
}
