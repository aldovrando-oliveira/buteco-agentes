namespace Buteco.Inbox.Contacts.Entities;

public class Session
{
    public Guid Id { get; private set; }

    public Guid ContactId { get; private set; }

    // string, não Guid nativo — casa com A2ATaskRecord.ContextId (apps/api),
    // que é string (design.md, Decision 2). Gerado localmente por
    // apps/inbox no momento da criação da Session; só "nasce" de verdade no
    // protocolo A2A quando um adapter real chamar SendMessage (fora do
    // escopo desta fatia).
    public string ContextId { get; private set; } = null!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    private Session()
    {
    }

    public Session(Guid contactId)
    {
        Id = Guid.NewGuid();
        ContactId = contactId;
        ContextId = Guid.NewGuid().ToString();
        StartedAt = DateTimeOffset.UtcNow;
        LastActivityAt = StartedAt;
    }

    public void RegisterActivity()
    {
        LastActivityAt = DateTimeOffset.UtcNow;
    }

    public void Close(DateTimeOffset closedAt)
    {
        ClosedAt = closedAt;
    }
}
