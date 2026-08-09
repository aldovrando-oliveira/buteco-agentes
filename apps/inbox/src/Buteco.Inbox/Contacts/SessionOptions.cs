namespace Buteco.Inbox.Contacts;

public sealed class SessionOptions
{
    public const string SectionName = "Session";

    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromHours(1);
}
