using System.Text.Json.Serialization;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// Shape confirmado via investigação contra a documentação real do Telegram
// (design.md, Context) — só os campos efetivamente lidos por
// TelegramInboundWebhookHandler, mesmo padrão minimalista de
// WahaWebhookMessagePayload. Update tem outros campos mutuamente
// exclusivos com Message (callback_query, edited_message, etc.) — nenhum
// deles é modelado aqui, e a ausência de Message já é suficiente para
// ignorá-los (design.md, Decision 8). Desserializado com
// PropertyNameCaseInsensitive; FirstName usa JsonPropertyName explícito
// porque o case-insensitive não remove o "_" de "first_name".
internal sealed record TelegramUpdate(TelegramMessage? Message);

internal sealed record TelegramMessage(TelegramChat? Chat, TelegramUser? From, string? Text);

internal sealed record TelegramChat(long Id);

internal sealed record TelegramUser(
    string? Username,
    [property: JsonPropertyName("first_name")] string? FirstName);
