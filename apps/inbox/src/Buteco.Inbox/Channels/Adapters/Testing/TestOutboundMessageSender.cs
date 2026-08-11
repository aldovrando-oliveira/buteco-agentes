namespace Buteco.Inbox.Channels.Adapters.Testing;

// Adapter de teste desta fatia (design.md, Decision 6) — só captura a
// chamada, sem nenhuma integração de rede real. Singleton (registrado via
// AddKeyedSingleton em Program.cs), então as mensagens capturadas ficam
// visíveis para inspeção em teste através da mesma instância resolvida
// pelo container. Guarda todas as chamadas (não só a última) porque a
// instância é compartilhada entre testes na mesma fixture — cada teste
// deve localizar a própria mensagem pelo ContactExternalId que gerou.
public sealed class TestOutboundMessageSender : IOutboundMessageSender
{
    private readonly List<OutboundMessage> _capturedMessages = [];
    private readonly object _gate = new();

    public IReadOnlyList<OutboundMessage> CapturedMessages
    {
        get
        {
            lock (_gate)
            {
                return _capturedMessages.ToList();
            }
        }
    }

    public Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _capturedMessages.Add(message);
        }

        return Task.CompletedTask;
    }
}
