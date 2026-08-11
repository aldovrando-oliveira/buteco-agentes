using System.Net.Http.Json;
using System.Text.Json;

namespace Buteco.Inbox.Channels.Adapters.Waha;

// design.md, Decision 7: POST {ServiceUrl}/api/sendText, X-Api-Key,
// chatId = message.ContactExternalId sem transformação (consequência
// direta da Decision 5 — ExternalId mantém o sufixo @c.us/@g.us).
// Client anônimo (não AddHttpClient<T> nomeado): BaseAddress é por
// credencial/canal, não fixa por adapter — mesmo padrão de
// A2AClientFactory, que também monta a Uri completa por chamada.
public sealed class WahaOutboundMessageSender(IHttpClientFactory httpClientFactory) : IOutboundMessageSender
{
    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var credential = JsonSerializer.Deserialize<WahaCredential>(message.DecryptedCredential)!;

        using var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(credential.ServiceUrl);
        client.DefaultRequestHeaders.Add("X-Api-Key", credential.AuthToken);

        var body = new { session = credential.SessionName, chatId = message.ContactExternalId, text = message.ResponseText };
        var response = await client.PostAsJsonAsync("/api/sendText", body, cancellationToken);

        // EnsureSuccessStatusCode lança em resposta não-2xx — a exceção
        // propaga até PushNotificationEndpoints.DeliverResponseAsync, que já
        // só loga (Non-Goal de retry herdado, comportamento inalterado por
        // esta fatia).
        response.EnsureSuccessStatusCode();
    }
}
