using System.Net;
using System.Text;

namespace Buteco.Workers.Tests.Notifications.Support;

public sealed record RecordedWebhookCall(
    Uri Url,
    HttpMethod Method,
    string Body,
    IReadOnlyDictionary<string, IEnumerable<string>> Headers);

/// <summary>
/// Simula o "webhook do cliente" que registrou um <c>PushNotificationConfig</c>
/// — injetado no lugar do <see cref="HttpMessageHandler"/> primário do
/// <see cref="HttpClient"/> nomeado usado por
/// <see cref="Buteco.Workers.Notifications.PushNotificationSender"/>, sem rede
/// real. Mesmo espírito de <c>FakeMcpServerHttpMessageHandler</c>.
/// </summary>
public sealed class FakeWebhookHttpMessageHandler : HttpMessageHandler
{
    private readonly List<RecordedWebhookCall> _calls = [];

    public IReadOnlyList<RecordedWebhookCall> Calls => _calls;

    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>
    /// Quando <c>true</c>, simula o timeout do próprio <see cref="HttpClient"/>
    /// (o que aconteceria de verdade quando o servidor não responde dentro do
    /// timeout configurado) lançando <see cref="TaskCanceledException"/>
    /// imediatamente, sem esperar o timeout real de 5s — mantém o teste
    /// rápido enquanto exercita o mesmo caminho de exceção que
    /// <see cref="Buteco.Workers.Notifications.PushNotificationSender"/>
    /// precisa capturar sem relançar.
    /// </summary>
    public bool SimulateTimeout { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request sem RequestUri.");
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value);

        _calls.Add(new RecordedWebhookCall(uri, request.Method, body, headers));

        if (SimulateTimeout)
        {
            throw new TaskCanceledException("Simulated webhook timeout.");
        }

        return new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8),
        };
    }
}
