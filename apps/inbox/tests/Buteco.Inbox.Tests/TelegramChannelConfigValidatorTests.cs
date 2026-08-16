using System.Text.Json;
using Buteco.Inbox.Channels.Adapters.Telegram;

namespace Buteco.Inbox.Tests;

public class TelegramChannelConfigValidatorTests
{
    private readonly TelegramChannelConfigValidator _validator = new();

    private static string CredentialJson(string? botToken = "123456:ABC-DEF") =>
        JsonSerializer.Serialize(new TelegramCredential(botToken!));

    [Fact]
    public void Validate_WithValidCredential_ReturnsSuccess()
    {
        var result = _validator.Validate(CredentialJson());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        var result = _validator.Validate("isto não é json");

        Assert.False(result.IsValid);
        Assert.Contains("credential", result.Errors.Keys);
    }

    [Fact]
    public void Validate_WithEmptyBotToken_ReturnsFailure()
    {
        var result = _validator.Validate(CredentialJson(botToken: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("BotToken"));
    }

    [Fact]
    public void Validate_WithWhitespaceBotToken_ReturnsFailure()
    {
        var result = _validator.Validate(CredentialJson(botToken: "   "));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("BotToken"));
    }

    [Fact]
    public void Validate_WithMissingBotTokenField_ReturnsFailure()
    {
        var result = _validator.Validate("{}");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("BotToken"));
    }
}
