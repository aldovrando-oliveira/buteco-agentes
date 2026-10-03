namespace Buteco.Connectors.Tests.Support;

// Relógio controlado pelo teste, sem pacote novo (o apps/inbox não usa
// Microsoft.Extensions.TimeProvider.Testing).
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
