using System.Text.Json.Serialization;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// Shape confirmado via investigação contra a documentação real do Telegram
// (design.md, Context) — só os campos efetivamente lidos por
// TelegramInboundWebhookHandler, mesmo padrão minimalista de
// WahaWebhookMessagePayload. Update tem outros campos mutuamente
// exclusivos com Message (callback_query, edited_message, etc.) — nenhum
// deles é modelado aqui, e a ausência de Message já é suficiente para
// ignorá-los (design.md, Decision 8). Desserializado com
// PropertyNameCaseInsensitive; FirstName/MessageId usam JsonPropertyName
// explícito porque o case-insensitive não remove o "_" de "first_name"/
// "message_id".
internal sealed record TelegramUpdate(TelegramMessage? Message);

// Photo/Voice/Document/Audio só para detectar presença de mídia
// (inbox-mensagens-persistidas, design.md, Decisão 8) — shape interno não
// lido, então object? basta (STJ desserializa como JsonElement). Caption é
// o campo de texto que acompanha mídia — Text é null nesse caso.
internal sealed record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    TelegramChat? Chat,
    TelegramUser? From,
    string? Text,
    string? Caption,
    object? Photo,
    object? Voice,
    object? Document,
    object? Audio);

internal sealed record TelegramChat(long Id);

internal sealed record TelegramUser(
    string? Username,
    [property: JsonPropertyName("first_name")] string? FirstName);
