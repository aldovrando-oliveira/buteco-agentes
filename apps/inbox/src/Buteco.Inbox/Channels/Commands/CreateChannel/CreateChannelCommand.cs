using Mediator;

namespace Buteco.Inbox.Channels.Commands.CreateChannel;

public sealed record CreateChannelCommand(string ChannelType, string Name, string Credential, Guid AgentId) : ICommand<CreateChannelResult>;
