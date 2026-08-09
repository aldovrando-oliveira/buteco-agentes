using Mediator;

namespace Buteco.Inbox.Channels.Commands.UpdateChannel;

public sealed record UpdateChannelCommand(Guid Id, string Name, string? Credential, Guid AgentId) : ICommand<UpdateChannelResult>;
