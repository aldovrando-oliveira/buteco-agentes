namespace Buteco.Connectors.Tests.Support;

// Relógio controlado pelo teste, sem pacote novo (o apps/inbox não usa
// Microsoft.Extensions.TimeProvider.Testing).
//
// Com timers desde a change ciclo-de-sincronizacao (design.md, D11): sem CreateTimer, um
// PeriodicTimer(intervalo, este relógio) cairia no timer REAL do sistema, e o teste do
// agendamento passaria a depender de espera de verdade. Os timers só disparam em
// Advance, de forma síncrona e na ordem do vencimento.
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>Quantas vezes algum timer deste relógio disparou.</summary>
    public int TimerFirings { get; private set; }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_gate)
        {
            target = _now.Add(by);
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.Due is { } due && due <= target)
                    .OrderBy(timer => timer.Due)
                    .FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? next.Due + next.Period : null;
                TimerFirings++;
            }

            next.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public TimeSpan Period { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now.Add(dueTime);
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            }

            return true;
        }

        public void Dispose() => clock.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
