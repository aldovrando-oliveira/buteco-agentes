namespace Buteco.Inbox.Channels.Adapters;

public interface IChannelConfigValidator
{
    ChannelConfigValidationResult Validate(string credential);
}
