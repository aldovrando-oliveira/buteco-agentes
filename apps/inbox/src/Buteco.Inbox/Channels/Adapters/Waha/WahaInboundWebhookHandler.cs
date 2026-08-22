using System.Text.Json;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Channels.Adapters.Waha;

// design.md, Decision 5: filtra por event == "message" (ignora outros
// eventos, ex. session.status, sem erro — não é uma requisição inválida,
// só não é processável por este handler); extrai o texto de payload.body
// (não payload.text); usa payload.from bruto, incluindo o sufixo @c.us,
// como ExternalId — WahaOutboundMessageSender usa esse valor diretamente
// como chatId, sem reconstrução. Nenhum filtro defensivo por fromMe: o
// evento "message" (diferente de "message.any") nunca inclui mensagens da
// própria sessão (ver design.md, Context) — garantia estrutural do WAHA,
// não deste código.
//
// IServiceScopeFactory, não IInboundMessageOrchestrator direto no
// construtor: este handler é registrado AddKeyedSingleton (tasks.md 8.1,
// mesmo padrão dos outros dois contratos WAHA), mas IInboundMessageOrchestrator
// é Scoped (depende de AppDbContext) — injetar o Scoped direto no
// construtor de um Singleton seria captive dependency, rejeitada por
// ValidateOnBuild. Um escopo próprio por chamada resolve o Scoped
// corretamente sem mudar a vida útil deste handler.
public sealed class WahaInboundWebhookHandler(IServiceScopeFactory scopeFactory) : IInboundWebhookHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task HandleAsync(Guid channelId, HttpRequest request, CancellationToken cancellationToken)
    {
        var envelope = await JsonSerializer.DeserializeAsync<WahaWebhookEnvelope>(request.Body, JsonOptions, cancellationToken);

        var payload = envelope?.Payload;
        if (envelope?.Event != "message" || payload?.Id is null || payload.From is null)
        {
            return;
        }

        // Mensagem de mídia sem legenda tem Body nulo/vazio — não descartar
        // nesse caso (inbox-mensagens-persistidas, design.md, Decisão 8).
        var hasText = !string.IsNullOrEmpty(payload.Body);
        if (!hasText && !payload.HasMedia)
        {
            return;
        }

        var contentType = ResolveContentType(payload);
        var content = hasText ? payload.Body! : $"[mídia: {contentType.ToString().ToLowerInvariant()}]";

        var externalId = payload.From;
        var phone = externalId.Split('@')[0];
        var displayName = payload.Data?.Info?.PushName;

        using var scope = scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();

        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            content,
            contentType,
            payload.Id,
            displayName,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["phone"] = phone },
            cancellationToken);
    }

    private static MessageContentType ResolveContentType(WahaWebhookMessagePayload payload)
    {
        if (!payload.HasMedia)
        {
            return MessageContentType.Text;
        }

        return payload.Media?.Mimetype switch
        {
            { } mimetype when mimetype.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => MessageContentType.Image,
            { } mimetype when mimetype.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => MessageContentType.Audio,
            _ => MessageContentType.Document,
        };
    }
}
