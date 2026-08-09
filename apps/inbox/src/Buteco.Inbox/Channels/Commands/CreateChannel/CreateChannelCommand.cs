using Buteco.Inbox.Channels.Entities;
using Mediator;

namespace Buteco.Inbox.Channels.Commands.CreateChannel;

public sealed record CreateChannelCommand(ChannelType ChannelType, string Name, string Credential, Guid AgentId) : ICommand<CreateChannelResult>;
