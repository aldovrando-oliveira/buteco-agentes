namespace Buteco.Inbox.Channels.Entities;

public class Channel
{
    public Guid Id { get; private set; }

    public ChannelType ChannelType { get; private set; }

    public string Name { get; private set; } = null!;

    // Já criptografada (ver IChannelCredentialCipher) — a entidade nunca vê
    // o valor em texto claro. Sempre presente: diferente de McpServer, todo
    // Channel precisa de credenciais para se conectar à plataforma externa.
    public string EncryptedCredentials { get; private set; } = null!;

    // Guid opaco — apps/inbox não compartilha banco com apps/api (design.md,
    // Decision 1), então não há FK possível. Validado via HTTP no momento do
    // cadastro/atualização (IAgentReferenceValidator), não aqui.
    public Guid AgentId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Channel()
    {
    }

    public Channel(ChannelType channelType, string name, string encryptedCredentials, Guid agentId)
    {
        Id = Guid.NewGuid();
        ChannelType = channelType;
        Name = name;
        EncryptedCredentials = encryptedCredentials;
        AgentId = agentId;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // ChannelType não é atualizável (spec: "Atualização de canal de
    // entrada" só cobre nome, credenciais e AgentId) — trocar de canal é
    // um novo cadastro, não uma edição do existente.
    public void UpdateDetails(string name, Guid agentId)
    {
        Name = name;
        AgentId = agentId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEncryptedCredentials(string encryptedCredentials)
    {
        EncryptedCredentials = encryptedCredentials;
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
