namespace Buteco.Api.Messaging;

public sealed record TaskJobMessage(string TaskId, Guid AgentId, string ContextId);
