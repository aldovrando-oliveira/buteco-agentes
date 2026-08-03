namespace Buteco.Workers.Mcp.Security;

public sealed class McpCryptoOptions
{
    public const string SectionName = "Mcp";

    public string? CredentialEncryptionKey { get; set; }
}
