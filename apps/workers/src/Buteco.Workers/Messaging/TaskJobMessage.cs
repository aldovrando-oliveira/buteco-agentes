using A2A;

namespace Buteco.Workers.Messaging;

public sealed record TaskJobMessage(
    string TaskId,
    Guid AgentId,
    string ContextId,
    PushNotificationConfig? PushNotificationConfig = null);
