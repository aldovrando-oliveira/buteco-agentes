using Buteco.Api.Identity.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O serviço da limpeza dirigido pelo relógio (design.md da change
/// limpeza-tokens-openiddict, D4), com um pruner falso: nenhum ciclo antes do primeiro
/// tique, um por tique, e a falha de um ciclo vira log sem parar o serviço (convenção 4).
/// </summary>
public class IdentityTokenPruneServiceTests
{
    private static readonly TimeSpan Interval = IdentityTokenPrunePolicy.Interval;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    private readonly CountingPruner _pruner = new();
    private readonly RecordingLogger _logger = new();

    [Fact]
    public async Task NoCycleAtStart_ThenOneCyclePerTick()
    {
        using var service = Service();
        await service.StartAsync(CancellationToken.None);

        _clock.Advance(Interval - TimeSpan.FromSeconds(1));
        await Task.Delay(100);
        Assert.Equal(0, _pruner.Calls);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await _pruner.WaitForCallsAsync(1);

        _clock.Advance(Interval);
        await _pruner.WaitForCallsAsync(2);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(2, _pruner.Calls);
    }

    [Fact]
    public async Task AFailedCycle_IsLogged_AndTheNextOneRuns()
    {
        _pruner.FailNext = true;
        using var service = Service();
        await service.StartAsync(CancellationToken.None);

        _clock.Advance(Interval);
        await _pruner.WaitForCallsAsync(1);
        await WaitForAsync(() => _logger.Has(IdentityTokenPruneService.PruneCycleFailedEvent, LogLevel.Error));

        _clock.Advance(Interval);
        await _pruner.WaitForCallsAsync(2);
        await WaitForAsync(() => _logger.Has(IdentityTokenPruneService.PrunedEvent, LogLevel.Information));

        await service.StopAsync(CancellationToken.None);
    }

    private IdentityTokenPruneService Service() =>
        new(_pruner, new IdentityTokenPruneSchedule(), _clock, _logger);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "o serviço não produziu o efeito esperado em 10 s");
            await Task.Delay(20);
        }
    }

    private sealed class CountingPruner : IIdentityTokenPruner
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool FailNext { get; set; }

        public Task<IdentityTokenPruneResult> RunCycleAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("banco fora do ar");
            }

            return Task.FromResult(IdentityTokenPruneResult.Pruned(tokens: 3, authorizations: 1));
        }

        public async Task WaitForCallsAsync(int expected)
        {
            await WaitForAsync(() => Calls >= expected);
        }
    }

    private sealed class RecordingLogger : ILogger<IdentityTokenPruneService>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(EventId EventId, LogLevel Level)> _entries = new();

        public bool Has(EventId eventId, LogLevel level) =>
            _entries.Any(entry => entry.EventId.Id == eventId.Id && entry.Level == level);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue((eventId, logLevel));
    }
}
