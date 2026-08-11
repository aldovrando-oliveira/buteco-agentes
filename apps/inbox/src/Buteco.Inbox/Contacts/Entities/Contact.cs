namespace Buteco.Inbox.Contacts.Entities;

public class Contact
{
    public Guid Id { get; private set; }

    public Guid ChannelId { get; private set; }

    // Identificador do contato na plataforma de origem (ex. número de
    // telefone, chat_id) — texto plano, risco documentado conscientemente
    // (design.md, Decision 5): precisa ser buscável/único por igualdade,
    // incompatível com a cifra de nonce aleatório já usada para
    // Channel.EncryptedCredentials.
    public string ExternalId { get; private set; } = null!;

    // Dicionário genérico (não um campo específico, ex. PhoneNumber) —
    // ponto de extensão para dado adicional de exibição/CRM que o adapter
    // de origem capture na criação, sem amarrar o schema de Contact a um
    // conceito de um único tipo de canal (inbox-adapter-waha, design.md,
    // Decision 8). Gravado só na criação — mesmo tratamento imutável que
    // ExternalId já recebe hoje, não há Update para este campo.
    public IReadOnlyDictionary<string, string> Metadata { get; private set; } = new Dictionary<string, string>();

    public DateTimeOffset CreatedAt { get; private set; }

    private Contact()
    {
    }

    public Contact(Guid channelId, string externalId, IReadOnlyDictionary<string, string> metadata)
    {
        Id = Guid.NewGuid();
        ChannelId = channelId;
        ExternalId = externalId;
        Metadata = metadata;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
