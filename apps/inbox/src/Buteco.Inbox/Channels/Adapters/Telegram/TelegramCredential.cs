namespace Buteco.Inbox.Channels.Adapters.Telegram;

// Sem ServiceUrl (sempre https://api.telegram.org, nunca configurável por
// canal) nem SessionName (o Telegram não distingue sessão de bot — um
// BotToken já identifica univocamente o destino de toda chamada) — mais
// simples que WahaCredential de propósito, não por descuido (design.md,
// Decision 7). WebhookSecret é opcional: só existe depois do primeiro
// provisionamento bem-sucedido (TelegramWebhookProvisioner) — o shape
// enviado pelo cliente em POST/PUT /channels nunca inclui esse campo.
public sealed record TelegramCredential(string BotToken, string? WebhookSecret = null);
