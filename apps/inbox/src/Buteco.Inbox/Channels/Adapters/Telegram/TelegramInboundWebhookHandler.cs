using System.Text.Json;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// design.md, Decision 8: primeiro adapter do catálogo a verificar
// autenticidade do webhook de entrada (secret_token nativo do Telegram) e
// a dar efeito prático a IsActive sobre o processamento de mensagens —
// ambos escopados a este adapter, sem alterar WebhookEndpoints/WAHA além
// do ajuste de plumbing em WebhookEndpoints.ReceiveAsync (Decision 8,
// "Detalhe de implementação").
//
// IServiceScopeFactory, não AppDbContext/IInboundMessageOrchestrator
// direto no construtor: este handler é AddKeyedSingleton (mesmo padrão do
// WahaInboundWebhookHandler), mas ambos são Scoped. IChannelCredentialCipher
// é Singleton (Program.cs), então entra direto no construtor.
public sealed class TelegramInboundWebhookHandler(
    IServiceScopeFactory scopeFactory,
    IChannelCredentialCipher credentialCipher) : IInboundWebhookHandler
{
    private const string SecretTokenHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Consulta redundante com a que WebhookEndpoints.ReceiveAsync já fez
        // para resolver o handler pelo ChannelType — aceito de propósito
        // (design.md, Decision 8, "Alternativa descartada"): mudar
        // IInboundWebhookHandler para receber o Channel inteiro afetaria
        // todo adapter existente e futuro só para evitar uma consulta by-id
        // barata.
        var channel = await dbContext.Channels.FindAsync([channelId], cancellationToken);
        if (channel is null)
        {
            request.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // IsActive antes do secret_token: um canal desativado responde 401
        // mesmo com o secret correto, sem ramificar em texto de erro
        // diferente por esse caminho (design.md, Decision 8).
        if (!channel.IsActive)
        {
            request.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var credential = JsonSerializer.Deserialize<TelegramCredential>(credentialCipher.Decrypt(channel.EncryptedCredentials))!;
        var receivedSecret = request.Headers[SecretTokenHeaderName].FirstOrDefault();
        if (receivedSecret is null || receivedSecret != credential.WebhookSecret)
        {
            request.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var update = await JsonSerializer.DeserializeAsync<TelegramUpdate>(request.Body, JsonOptions, cancellationToken);
        var message = update?.Message;
        if (message?.Chat is null)
        {
            return;
        }

        // Mensagem de mídia carrega o texto em Caption, não Text — não
        // descartar esse caso (inbox-mensagens-persistidas, design.md,
        // Decisão 8).
        var contentType = ResolveContentType(message);
        var text = message.Text ?? message.Caption;
        if (text is null && contentType == MessageContentType.Text)
        {
            return;
        }

        var content = text ?? $"[mídia: {contentType.ToString().ToLowerInvariant()}]";

        var externalId = message.Chat.Id.ToString();
        var metadata = new Dictionary<string, string>();
        if (message.From?.Username is { } username)
        {
            metadata["username"] = username;
        }

        if (message.From?.FirstName is { } firstName)
        {
            metadata["firstName"] = firstName;
        }

        var displayName = message.From?.Username ?? message.From?.FirstName;

        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            content,
            contentType,
            message.MessageId.ToString(),
            displayName,
            DateTimeOffset.UtcNow,
            metadata,
            cancellationToken);
    }

    private static MessageContentType ResolveContentType(TelegramMessage message) =>
        message switch
        {
            { Photo: not null } => MessageContentType.Image,
            { Voice: not null } or { Audio: not null } => MessageContentType.Audio,
            { Document: not null } => MessageContentType.Document,
            _ => MessageContentType.Text,
        };
}
