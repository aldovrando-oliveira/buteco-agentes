namespace Buteco.Api.McpServers.Security;

public sealed class McpCryptoOptions
{
    public const string SectionName = "Mcp";

    public string? CredentialEncryptionKey { get; set; }
}
