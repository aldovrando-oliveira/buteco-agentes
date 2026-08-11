namespace Buteco.Inbox.Channels.Adapters.Testing;

// Adapter de teste desta fatia (inbox-adapter-waha, design.md, Decision 1) —
// completa o terceiro contrato para "test-channel", registrado desde
// inbox-adapter-contrato-catalogo. Sem essa terceira peça,
// ValidateChannelAdapterRegistrations (agora estendida para três
// contratos) derrubaria o processo no startup por composição incompleta de
// "test-channel" — usado por toda a suíte de testes de integração que sobe
// o host real (InboxFactoryFixture/OrchestrationFactoryFixture). Só
// captura a chamada, sem nenhuma integração de rede real, mesmo padrão de
// TestOutboundMessageSender.
public sealed class TestInboundWebhookHandler : IInboundWebhookHandler
{
    private readonly List<Guid> _handledChannelIds = [];
    private readonly object _gate = new();

    public IReadOnlyList<Guid> HandledChannelIds
    {
        get
        {
            lock (_gate)
            {
                return _handledChannelIds.ToList();
            }
        }
    }

    public Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _handledChannelIds.Add(channelId);
        }

        return Task.CompletedTask;
    }
}
