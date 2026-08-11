using System.Text.Json;
using Buteco.Inbox.Channels.Adapters.Waha;

namespace Buteco.Inbox.Tests;

public class WahaChannelConfigValidatorTests
{
    private readonly WahaChannelConfigValidator _validator = new();

    private static string ValidCredentialJson(string serviceUrl = "http://localhost:3000", string sessionName = "default", string authToken = "s3cr3t") =>
        JsonSerializer.Serialize(new WahaCredential(serviceUrl, sessionName, authToken));

    [Fact]
    public void Validate_WithValidCredential_ReturnsSuccess()
    {
        var result = _validator.Validate(ValidCredentialJson());

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
    public void Validate_WithRelativeServiceUrl_ReturnsFailure()
    {
        // Sem esquema e sem barra inicial — "/relativo" seria interpretado
        // por Uri.TryCreate(..., UriKind.Absolute, ...) como um caminho de
        // arquivo absoluto (file://) em vez de rejeitado.
        var result = _validator.Validate(ValidCredentialJson(serviceUrl: "relativo-sem-esquema"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("serviceUrl"));
    }

    [Fact]
    public void Validate_WithEmptySessionName_ReturnsFailure()
    {
        var result = _validator.Validate(ValidCredentialJson(sessionName: "  "));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("sessionName"));
    }

    [Fact]
    public void Validate_WithEmptyAuthToken_ReturnsFailure()
    {
        var result = _validator.Validate(ValidCredentialJson(authToken: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors["credential"], message => message.Contains("authToken"));
    }
}
