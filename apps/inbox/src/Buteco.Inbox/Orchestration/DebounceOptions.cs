namespace Buteco.Inbox.Orchestration;

public sealed class DebounceOptions
{
    public const string SectionName = "Debounce";

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int MaxDispatchAttempts { get; set; } = 3;
}
