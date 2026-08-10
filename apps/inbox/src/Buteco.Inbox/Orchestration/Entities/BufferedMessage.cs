namespace Buteco.Inbox.Orchestration.Entities;

public sealed class BufferedMessage
{
    public string Text { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    private BufferedMessage()
    {
    }

    public BufferedMessage(string text, DateTimeOffset receivedAt)
    {
        Text = text;
        ReceivedAt = receivedAt;
    }
}
