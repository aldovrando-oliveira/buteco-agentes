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

    // Nome do header em que a credencial vai quando AuthType == CustomHeader.
    // Configuração, não segredo: fica em claro e volta na resposta (D2 da change
    // mcp-autenticacao-header-customizado). Nulo em qualquer outro tipo.
    public string? AuthHeaderName { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private McpServer()
    {
    }

    public McpServer(string name, string description, string url, McpServerAuthType authType, string? encryptedCredential, string? authHeaderName = null)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        Url = url;
        AuthType = authType;
        EncryptedCredential = authType == McpServerAuthType.None ? null : encryptedCredential;
        AuthHeaderName = authType == McpServerAuthType.CustomHeader ? authHeaderName : null;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // Não recebe a credencial aqui — ver SetEncryptedCredential.
    //
    // A credencial persistida está VINCULADA AO TIPO para o qual foi informada
    // (D4 da change mcp-autenticacao-header-customizado), e por isso é limpa
    // sempre que o AuthType MUDA — não só quando ele vai para None. Isso estende
    // a Decision 6 da change backend-mcp-catalogo-vinculo, que limpava só na
    // transição para None (segredo cifrado sem uso possível). O motivo da
    // extensão: um token digitado para Authorization: Bearer, mandado cru em
    // X-API-TOKEN (ou o inverso), falharia como "credencial rejeitada" sem
    // pista de que o valor era do tipo anterior.
    //
    // Quem produz o 400 é o handler, não este método: com a credencial limpa e
    // nenhuma nova informada, a checagem "AuthType != None && sem credencial"
    // de UpdateMcpServerCommandHandler devolve CredentialMissing ANTES do
    // SaveChanges, e o registro persistido fica intacto.
    public void UpdateDetails(string name, string description, string url, McpServerAuthType authType, string? authHeaderName)
    {
        var authTypeChanged = authType != AuthType;

        Name = name;
        Description = description;
        Url = url;
        AuthType = authType;
        AuthHeaderName = authType == McpServerAuthType.CustomHeader ? authHeaderName : null;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (authTypeChanged || authType == McpServerAuthType.None)
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
