using System.Collections.Concurrent;

namespace InboxOrchestratorRoundTrip.Tests.Support;

public enum PushNotificationGateMode
{
    // Repassa ao TestServer de apps/inbox.
    PassThrough,

    // Falha de rede antes de alcançar apps/inbox (fonte 2 da #47).
    Fail,

    // Segura a chamada até o token dela ser cancelado — a parada do worker
    // (fonte 3 da #47). Avisa em Entered quando a chamada chega aqui.
    HoldUntilCancelled,
}

/// <summary>
/// Controle do webhook de push notification de apps/workers no round-trip,
/// e registro de cada chamada que passou por ele.
/// </summary>
public sealed class PushNotificationGate
{
    public PushNotificationGateMode Mode { get; set; } = PushNotificationGateMode.PassThrough;

    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> Outcomes { get; } = new();

    public void Reset(PushNotificationGateMode mode)
    {
        Mode = mode;
        Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class PushNotificationGateHandler(PushNotificationGate gate) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        switch (gate.Mode)
        {
            case PushNotificationGateMode.Fail:
                gate.Outcomes.Enqueue("failed");
                throw new HttpRequestException("Falha simulada de rede entre apps/workers e apps/inbox.");

            case PushNotificationGateMode.HoldUntilCancelled:
                gate.Entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                finally
                {
                    gate.Outcomes.Enqueue("cancelled");
                }

                throw new OperationCanceledException(cancellationToken);

            default:
                var response = await base.SendAsync(request, cancellationToken);
                gate.Outcomes.Enqueue($"delivered:{(int)response.StatusCode}");
                return response;
        }
    }
}
