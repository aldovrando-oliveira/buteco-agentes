namespace Buteco.Inbox.Channels.Adapters;

public sealed record OutboundMessage(
    Guid ChannelId,
    string DecryptedCredential,
    string ContactExternalId,
    string ResponseText);
