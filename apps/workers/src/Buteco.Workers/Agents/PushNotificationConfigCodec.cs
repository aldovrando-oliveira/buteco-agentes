using System.Text.Json;
using A2A;

namespace Buteco.Workers.Agents;

/// <summary>
/// Codifica o <c>PushNotificationConfig</c> recebido via
/// <see cref="Buteco.Workers.Messaging.TaskJobMessage"/> para gravação em
/// <c>AgentTask.Metadata</c> — mesmo padrão de <c>DelegationDepth</c>/
/// <c>ConversationSessionCodec</c> (design.md, Decision 1).
/// </summary>
public static class PushNotificationConfigCodec
{
    public const string MetadataKey = "pushNotificationConfig";

    public static JsonElement Encode(PushNotificationConfig config) =>
        JsonSerializer.SerializeToElement(config);
}
