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

    public DateTimeOffset CreatedAt { get; private set; }

    private Contact()
    {
    }

    public Contact(Guid channelId, string externalId)
    {
        Id = Guid.NewGuid();
        ChannelId = channelId;
        ExternalId = externalId;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
