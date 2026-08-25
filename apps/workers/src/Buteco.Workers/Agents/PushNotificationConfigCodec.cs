using System.Text.Json;
using A2A;

namespace Buteco.Workers.Agents;

/// <summary>
/// Codifica o <c>PushNotificationConfig</c> recebido via
/// <see cref="Buteco.Workers.Messaging.TaskJobMessage"/> para gravação em
/// <c>AgentTask.Metadata</c> — mesmo padrão de <c>DelegationDepth</c>/
/// <c>ConversationSessionCodec</c> (design.md, Decision 1).
/// </summary>
/// <remarks>
/// A codificação precisa usar <see cref="A2AJsonUtilities.DefaultOptions"/>
/// — as mesmas opções que o resto do pipeline A2A usa. Diferente de
/// <c>ConversationSessionCodec</c> (que codifica uma string escalar), este
/// <c>Encode</c> produz um <see cref="JsonElement"/> objeto, e a
/// re-serialização do <c>AgentTask</c> inteiro em
/// <c>PostgresTaskStore.SaveTaskAsync</c> não reaplica naming policy nem
/// <c>DefaultIgnoreCondition</c> sobre um <see cref="JsonElement"/> já
/// materializado — só sobre objetos .NET serializados a fresco. Usar as
/// opções padrão do .NET aqui grava o formato errado (casing PascalCase,
/// nulls explícitos) permanentemente em disco, não só divergência
/// temporária de encoding. Ver design.md da change
/// push-notification-config-codec-encoder, Decisões D1/D6.
/// </remarks>
public static class PushNotificationConfigCodec
{
    public const string MetadataKey = "pushNotificationConfig";

    public static JsonElement Encode(PushNotificationConfig config) =>
        JsonSerializer.SerializeToElement(config, A2AJsonUtilities.DefaultOptions);
}
