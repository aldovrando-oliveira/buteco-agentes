namespace Buteco.Inbox.Orchestration.Entities;

// Uma linha por Session com debounce em aberto — buffer persistido em
// Postgres, não em memória (design.md, Decisão 1). Ciclo de vida:
// Pending (bufferizando) -> Dispatching (reivindicada por uma instância,
// SendMessage em voo ou aguardando push notification) -> removida ao
// alcançar qualquer estado terminal (Decisão 6), ou de volta a Pending
// numa falha de transporte reintentável (Decisão 9).
public class PendingDispatch
{
    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public PendingDispatchStatus Status { get; private set; }

    public List<BufferedMessage> Messages { get; private set; } = [];

    public DateTimeOffset LastMessageAt { get; private set; }

    // Preenchido a partir da resposta síncrona do SendMessage quando o
    // status retornado é Submitted (design.md, Decisão 8) — usado pelo
    // endpoint receptor para localizar esta linha a partir do AgentTask.Id
    // recebido na push notification.
    public string? TaskId { get; private set; }

    // Gerado por chamada de SendMessage, persistido (não em memória —
    // design.md, Decisão 6) para o endpoint receptor validar contra o
    // header X-A2A-Notification-Token.
    public string? ExpectedToken { get; private set; }

    // Incrementado a cada falha de transporte reintentada (design.md,
    // Decisão 9) — ao atingir DebounceOptions.MaxDispatchAttempts, a
    // perda é considerada definitiva.
    public int AttemptCount { get; private set; }

    private PendingDispatch()
    {
    }

    public PendingDispatch(Guid sessionId, string text, DateTimeOffset receivedAt)
    {
        Id = Guid.NewGuid();
        SessionId = sessionId;
        Status = PendingDispatchStatus.Pending;
        Messages = [new BufferedMessage(text, receivedAt)];
        LastMessageAt = receivedAt;
        AttemptCount = 0;
    }

    public void AppendMessage(string text, DateTimeOffset receivedAt)
    {
        Messages.Add(new BufferedMessage(text, receivedAt));
        LastMessageAt = receivedAt;
    }

    public void MarkDispatching(string expectedToken)
    {
        Status = PendingDispatchStatus.Dispatching;
        ExpectedToken = expectedToken;
    }

    public void RegisterTaskId(string taskId)
    {
        TaskId = taskId;
    }

    // Volta para Pending — não é removida — para que o próprio
    // DebounceSweepService reapresente esta linha como candidata no
    // próximo ciclo, reaproveitando a janela de debounce como intervalo
    // de retry (design.md, Decisão 9). ExpectedToken é limpo: a
    // tentativa que falhou nunca vai gerar uma push notification.
    public void RegisterTransportFailure(DateTimeOffset failedAt)
    {
        AttemptCount++;
        Status = PendingDispatchStatus.Pending;
        LastMessageAt = failedAt;
        ExpectedToken = null;
    }

    public void MarkFailed()
    {
        Status = PendingDispatchStatus.Failed;
    }

    public string ConcatenatedText() =>
        string.Join('\n', Messages.Select(message => message.Text));
}
