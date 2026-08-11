namespace Buteco.Inbox.Channels.Adapters.Waha;

// Shape confirmado via investigação contra a documentação real do WAHA
// (design.md, Context) — deserializado com PropertyNameCaseInsensitive
// (WahaInboundWebhookHandler), então os nomes das propriedades aqui não
// precisam bater exatamente na caixa do JSON (camelCase) recebido.
internal sealed record WahaWebhookEnvelope(string? Event, string? Session, WahaWebhookMessagePayload? Payload);

// O campo de texto é Body — não Text (assimetria de nome com o corpo do
// sendText, ver design.md, Context/Decision 5). From/To no formato
// {numero}@c.us (indivíduo) ou {numero}@g.us (grupo).
internal sealed record WahaWebhookMessagePayload(string? From, string? To, string? Body, bool FromMe);
