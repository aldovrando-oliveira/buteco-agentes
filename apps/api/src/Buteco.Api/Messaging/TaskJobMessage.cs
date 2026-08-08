using A2A;

namespace Buteco.Api.Messaging;

public sealed record TaskJobMessage(
    string TaskId,
    Guid AgentId,
    string ContextId,
    PushNotificationConfig? PushNotificationConfig = null);
