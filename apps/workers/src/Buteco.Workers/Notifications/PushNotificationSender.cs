using System.Text;
using System.Text.Json;
using global::A2A;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Notifications;

/// <summary>
/// Dispara a chamada de webhook de push notification quando uma task
/// registrada com <see cref="PushNotificationConfig"/> atinge um estado
/// terminal — fire-and-forget com timeout curto (design.md, Decision 3).
/// Nenhum código do pacote A2A/A2A.AspNetCore faz isso; payload e headers
/// de autenticação (design.md, Decision 6) são responsabilidade nossa.
/// </summary>
public sealed class PushNotificationSender(IHttpClientFactory httpClientFactory, ILogger<PushNotificationSender> logger)
{
    public const string HttpClientName = "PushNotificationWebhook";

    public async Task SendAsync(PushNotificationConfig config, AgentTask task, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions),
                    Encoding.UTF8,
                    "application/json"),
            };

            if (config.Authentication is not null)
            {
                request.Headers.TryAddWithoutValidation(
                    "Authorization", $"{config.Authentication.Scheme} {config.Authentication.Credentials}");
            }

            if (config.Token is not null)
            {
                request.Headers.TryAddWithoutValidation("X-A2A-Notification-Token", config.Token);
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Webhook de push notification para {Url} respondeu {StatusCode} para a task {TaskId}",
                    config.Url, (int)response.StatusCode, task.Id);
            }
        }
        catch (Exception ex)
        {
            // Nunca relançar: falha de webhook (URL inalcançável, timeout,
            // resposta não-2xx já tratada acima) não pode bloquear nem
            // atrasar a conclusão da task, que já foi persistida antes desta
            // chamada (design.md, Decision 3).
            logger.LogWarning(
                ex, "Falha ao chamar o webhook de push notification para {Url} da task {TaskId}", config.Url, task.Id);
        }
    }
}
