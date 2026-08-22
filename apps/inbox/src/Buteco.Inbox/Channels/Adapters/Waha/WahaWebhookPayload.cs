using System.Text.Json.Serialization;

namespace Buteco.Inbox.Channels.Adapters.Waha;

// Shape confirmado via investigação contra a documentação real do WAHA
// (design.md, Context) — deserializado com PropertyNameCaseInsensitive
// (WahaInboundWebhookHandler), então os nomes das propriedades aqui não
// precisam bater exatamente na caixa do JSON (camelCase) recebido.
internal sealed record WahaWebhookEnvelope(string? Event, string? Session, WahaWebhookMessagePayload? Payload);

// O campo de texto é Body — não Text (assimetria de nome com o corpo do
// sendText, ver design.md, Context/Decision 5). From/To no formato
// {numero}@c.us (indivíduo) ou {numero}@g.us (grupo). Id é o identificador
// de mensagem documentado pelo WAHA (formato "{fromMe}_{chatId}_{msgId}"),
// usado para deduplicar webhook reentregue
// (inbox-mensagens-persistidas, design.md, Decisão 5).
internal sealed record WahaWebhookMessagePayload(
    string? Id,
    string? From,
    string? To,
    string? Body,
    bool FromMe,
    bool HasMedia,
    WahaMediaPayload? Media,
    [property: JsonPropertyName("_data")] WahaRawData? Data);

internal sealed record WahaMediaPayload(string? Mimetype);

// _data.Info.PushName — não documentado na doc oficial do WAHA (que declara
// _data como "dado interno do engine, pode variar por engine"); confirmado
// só via discussão da comunidade para o engine GOWS, o engine usado por
// este projeto (01-ARQUITETURA_E_CONVENCOES.md). Confiança menor que os
// demais campos deste arquivo — revisitar se o shape mudar entre versões
// do WAHA ou ao trocar de engine (inbox-mensagens-persistidas, design.md,
// Decisão 9).
internal sealed record WahaRawData(WahaMessageInfo? Info);

internal sealed record WahaMessageInfo(string? PushName);
