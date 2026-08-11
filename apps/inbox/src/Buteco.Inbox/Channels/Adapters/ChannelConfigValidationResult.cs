namespace Buteco.Inbox.Channels.Adapters;

public sealed record ChannelConfigValidationResult(bool IsValid, IReadOnlyDictionary<string, string[]> Errors)
{
    public static ChannelConfigValidationResult Success() =>
        new(true, new Dictionary<string, string[]>());

    public static ChannelConfigValidationResult Failure(IReadOnlyDictionary<string, string[]> errors) =>
        new(false, errors);
}
