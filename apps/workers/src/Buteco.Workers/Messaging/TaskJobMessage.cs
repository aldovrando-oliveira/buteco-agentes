namespace Buteco.Workers.Messaging;

public sealed record TaskJobMessage(string TaskId, Guid AgentId, string ContextId);
