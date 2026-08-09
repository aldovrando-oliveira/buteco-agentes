namespace Buteco.Inbox.Channels.Requests;

public record CreateChannelRequest(string? ChannelType, string? Name, string? Credential, Guid? AgentId);
