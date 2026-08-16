using System.Text.Json;

namespace Buteco.Inbox.Channels.Adapters.Telegram;

// design.md, Decision 7: credencial JSON decodificada e validada campo a
// campo, antes de ser cifrada por CreateChannelCommandHandler/
// UpdateChannelCommandHandler (contrato IChannelConfigValidator). Mesmo
// padrão de erro de WahaChannelConfigValidator.
public sealed class TelegramChannelConfigValidator : IChannelConfigValidator
{
    public ChannelConfigValidationResult Validate(string credential)
    {
        TelegramCredential? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TelegramCredential>(credential);
        }
        catch (JsonException)
        {
            return Fail("A credencial do Telegram deve ser um JSON com o campo BotToken.");
        }

        if (parsed is null)
        {
            return Fail("A credencial do Telegram deve ser um JSON com o campo BotToken.");
        }

        return string.IsNullOrWhiteSpace(parsed.BotToken)
            ? Fail("BotToken é obrigatório.")
            : ChannelConfigValidationResult.Success();
    }

    private static ChannelConfigValidationResult Fail(string message) =>
        ChannelConfigValidationResult.Failure(new Dictionary<string, string[]> { ["credential"] = [message] });
}
