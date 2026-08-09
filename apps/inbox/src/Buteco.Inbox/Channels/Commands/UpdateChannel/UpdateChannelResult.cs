using Buteco.Inbox.Channels.Responses;

namespace Buteco.Inbox.Channels.Commands.UpdateChannel;

public enum UpdateChannelOutcome
{
    Success,
    NotFound,
    AgentNotFound,
    AgentValidationFailed,
}

public sealed record UpdateChannelResult(ChannelResponse? Channel, UpdateChannelOutcome Outcome)
{
    public static UpdateChannelResult NotFound() => new(null, UpdateChannelOutcome.NotFound);

    public static UpdateChannelResult AgentNotFound() => new(null, UpdateChannelOutcome.AgentNotFound);

    public static UpdateChannelResult AgentValidationFailed() => new(null, UpdateChannelOutcome.AgentValidationFailed);

    public static UpdateChannelResult Success(ChannelResponse channel) => new(channel, UpdateChannelOutcome.Success);
}
