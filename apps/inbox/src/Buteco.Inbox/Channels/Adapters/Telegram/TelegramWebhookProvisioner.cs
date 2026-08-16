using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// design.md, Decision 9: gera um secret_token por chamada (nunca
// reaproveitado — design.md, Decision 6), chama setWebhook do Telegram, e
// devolve a credencial original com WebhookSecret preenchido. Checa
// sucesso via IsSuccessStatusCode/payload.Ok, nunca contra error_code/
// description específicos — a documentação oficial não fixa esses valores
// para token inválido (design.md, Context).
public sealed class TelegramWebhookProvisioner(IHttpClientFactory httpClientFactory) : IChannelWebhookProvisioner
{
    public async Task<ChannelWebhookProvisioningResult> ProvisionAsync(
        Guid channelId, string credential, string webhookUrl, CancellationToken cancellationToken)
    {
        var parsed = JsonSerializer.Deserialize<TelegramCredential>(credential)!;

        // 32 bytes (256 bits) de entropia via RandomNumberGenerator
        // (criptograficamente seguro). Convert.ToHexString gera só
        // 0-9/A-F — subconjunto do charset A-Z a-z 0-9 _ - aceito pelo
        // secret_token, sem necessidade de validar caracteres depois de
        // gerado. 64 caracteres, dentro do máximo de 256.
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        using var client = httpClientFactory.CreateClient();
        var body = new { url = webhookUrl, secret_token = secret };
        var response = await client.PostAsJsonAsync(
            $"https://api.telegram.org/bot{parsed.BotToken}/setWebhook", body, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TelegramApiResponse>(cancellationToken);
        if (!response.IsSuccessStatusCode || payload is not { Ok: true })
        {
            return ChannelWebhookProvisioningResult.Failed(payload?.Description ?? "Falha desconhecida ao chamar setWebhook do Telegram.");
        }

        return ChannelWebhookProvisioningResult.Succeeded(
            JsonSerializer.Serialize(parsed with { WebhookSecret = secret }));
    }

    private sealed record TelegramApiResponse(bool Ok, string? Description);
}
