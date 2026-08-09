namespace Buteco.Inbox.Channels.Security;

public sealed class InboxCryptoOptions
{
    public const string SectionName = "Inbox";

    public string? CredentialEncryptionKey { get; set; }
}
