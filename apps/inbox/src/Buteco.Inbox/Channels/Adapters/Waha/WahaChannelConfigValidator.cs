using System.Text.Json;

namespace Buteco.Inbox.Channels.Adapters.Waha;

// design.md, Decision 4: credencial JSON decodificada e validada campo a
// campo, antes de ser cifrada por CreateChannelCommandHandler/
// UpdateChannelCommandHandler (contrato IChannelConfigValidator).
public sealed class WahaChannelConfigValidator : IChannelConfigValidator
{
    public ChannelConfigValidationResult Validate(string credential)
    {
        WahaCredential? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WahaCredential>(credential);
        }
        catch (JsonException)
        {
            return Fail("A credencial do WAHA deve ser um JSON com os campos serviceUrl, sessionName e authToken.");
        }

        if (parsed is null)
        {
            return Fail("A credencial do WAHA deve ser um JSON com os campos serviceUrl, sessionName e authToken.");
        }

        var errors = new List<string>();

        if (!Uri.TryCreate(parsed.ServiceUrl, UriKind.Absolute, out _))
        {
            errors.Add("serviceUrl deve ser uma URL absoluta (ex. http://localhost:3000).");
        }

        if (string.IsNullOrWhiteSpace(parsed.SessionName))
        {
            errors.Add("sessionName é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(parsed.AuthToken))
        {
            errors.Add("authToken é obrigatório.");
        }

        return errors.Count > 0
            ? ChannelConfigValidationResult.Failure(new Dictionary<string, string[]> { ["credential"] = errors.ToArray() })
            : ChannelConfigValidationResult.Success();
    }

    private static ChannelConfigValidationResult Fail(string message) =>
        ChannelConfigValidationResult.Failure(new Dictionary<string, string[]> { ["credential"] = [message] });
}
