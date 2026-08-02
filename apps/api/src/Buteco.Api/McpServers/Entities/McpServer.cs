namespace Buteco.Api.McpServers.Entities;

public class McpServer
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string Url { get; private set; } = null!;

    public McpServerAuthType AuthType { get; private set; }

    // Já criptografada (ver IMcpCredentialCipher) — a entidade nunca vê o
    // valor em texto claro. Nula quando AuthType == None.
    public string? EncryptedCredential { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private McpServer()
    {
    }

    public McpServer(string name, string description, string url, McpServerAuthType authType, string? encryptedCredential)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        Url = url;
        AuthType = authType;
        EncryptedCredential = authType == McpServerAuthType.None ? null : encryptedCredential;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // Não recebe a credencial aqui — ver SetEncryptedCredential. Quando
    // authType transiciona para None, limpa a credencial anteriormente
    // persistida (Decision 6 do design.md da change
    // backend-mcp-catalogo-vinculo): não faz sentido manter um segredo
    // cifrado sem uso funcional possível.
    public void UpdateDetails(string name, string description, string url, McpServerAuthType authType)
    {
        Name = name;
        Description = description;
        Url = url;
        AuthType = authType;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (authType == McpServerAuthType.None)
        {
            EncryptedCredential = null;
        }
    }

    public void SetEncryptedCredential(string encryptedCredential)
    {
        EncryptedCredential = encryptedCredential;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
