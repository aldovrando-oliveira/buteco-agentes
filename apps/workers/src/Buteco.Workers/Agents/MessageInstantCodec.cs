using System.Text.Json;
using A2A;

namespace Buteco.Workers.Agents;

/// <summary>
/// Codifica o instante em que a mensagem do usuário foi originalmente
/// enviada para gravação em <c>Message.Metadata</c> — mesmo padrão de
/// <c>DelegationDepth</c>/<c>ConversationSessionCodec</c>/
/// <c>PushNotificationConfigCodec</c>. Chave e formato batem com o lado da
/// escrita em <c>apps/inbox</c> (<c>DebounceSweepService</c>) — duplicado
/// deliberadamente, apps isolados sem <c>ProjectReference</c> cruzado (ver
/// design.md da change inbox-instante-mensagem, Decisão D2).
/// </summary>
/// <remarks>
/// Valor sempre ESCALAR (string ISO 8601 com offset) — nunca objeto. É essa
/// escolha de shape, não a presença de <see cref="A2AJsonUtilities.DefaultOptions"/>
/// sozinha, que elimina por construção a variante séria do mecanismo em que
/// um <see cref="JsonElement"/> pré-materializado sobrevive à
/// re-serialização do <c>AgentTask</c> sem reaplicar naming policy — ver
/// design.md, Decisão D2, e a mesma classe de defeito documentada em
/// <see cref="PushNotificationConfigCodec"/>.
/// </remarks>
public static class MessageInstantCodec
{
    public const string MetadataKey = "messageInstant";

    public static JsonElement Encode(DateTimeOffset instant) =>
        JsonSerializer.SerializeToElement(instant.ToString("O"), A2AJsonUtilities.DefaultOptions);
}
