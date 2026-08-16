using Buteco.Inbox.Channels.Responses;

namespace Buteco.Inbox.Channels.Commands.UpdateChannel;

public enum UpdateChannelOutcome
{
    Success,
    NotFound,
    AgentNotFound,
    AgentValidationFailed,
    InvalidCredential,
    ProvisioningFailed,
}

public sealed record UpdateChannelResult(
    ChannelResponse? Channel,
    UpdateChannelOutcome Outcome,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ProvisioningError = null)
{
    public static UpdateChannelResult NotFound() => new(null, UpdateChannelOutcome.NotFound);

    public static UpdateChannelResult AgentNotFound() => new(null, UpdateChannelOutcome.AgentNotFound);

    public static UpdateChannelResult AgentValidationFailed() => new(null, UpdateChannelOutcome.AgentValidationFailed);

    public static UpdateChannelResult Success(ChannelResponse channel) => new(channel, UpdateChannelOutcome.Success);

    public static UpdateChannelResult InvalidCredential(IReadOnlyDictionary<string, string[]> errors) =>
        new(null, UpdateChannelOutcome.InvalidCredential, errors);

    public static UpdateChannelResult ProvisioningFailed(string errorMessage) =>
        new(null, UpdateChannelOutcome.ProvisioningFailed, ProvisioningError: errorMessage);
}
