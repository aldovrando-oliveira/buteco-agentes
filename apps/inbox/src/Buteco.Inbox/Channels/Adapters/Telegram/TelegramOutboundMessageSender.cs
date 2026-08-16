using System.Net.Http.Json;
using System.Text.Json;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// design.md, Decision 9: POST https://api.telegram.org/bot{BotToken}/sendMessage,
// corpo {chat_id, text}. chat_id = message.ContactExternalId sem
// transformação — ExternalId já é message.chat.id convertido para string
// (design.md, Decision 8), e o Telegram aceita chat_id como Integer ou
// String. Client anônimo (não AddHttpClient<T> nomeado): mesmo padrão de
// WahaOutboundMessageSender — o token vai embutido na URL, não em header,
// então não há BaseAddress fixa por adapter.
public sealed class TelegramOutboundMessageSender(IHttpClientFactory httpClientFactory) : IOutboundMessageSender
{
    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        var credential = JsonSerializer.Deserialize<TelegramCredential>(message.DecryptedCredential)!;

        using var client = httpClientFactory.CreateClient();
        var body = new { chat_id = message.ContactExternalId, text = message.ResponseText };
        var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{credential.BotToken}/sendMessage", body, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
