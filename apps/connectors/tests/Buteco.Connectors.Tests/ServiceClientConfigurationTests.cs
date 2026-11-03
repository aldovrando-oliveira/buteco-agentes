using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O segredo do <c>apps/connectors</c> no emissor, conferido no boot da aplicação real (spec
/// <c>service-client-credentials</c>, "Segredo do serviço obrigatório e conferido no boot";
/// design.md da change servicos-token-emissor, D5; convenção 8): com <c>Api:BaseUrl</c>,
/// presente, base64 e 32 bytes, sem ecoar o valor; sem <c>Api:BaseUrl</c>, dispensado.
/// </summary>
public class ServiceClientConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("isto-nao-e-base64-mas-e-longo-o-bastante!!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void WithTheApiAddress_AnInvalidSecret_FailsTheRealStartup_NamingTheSetting_WithoutTheValue(string secret)
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = "http://api.interno:8080",
            ["ServiceClient:Secret"] = secret,
        });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        var message = Flatten(exception);
        Assert.Contains(ServiceClientOptions.SecretConfigurationName, message, StringComparison.Ordinal);
        if (secret.Length > 0)
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WithoutTheApiAddress_TheSecretIsNotRequired()
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = "",
            ["ServiceClient:Secret"] = "",
        });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.Null(exception);
    }

    [Fact]
    public void WithTheApiAddress_AValidSecret_Boots()
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Api:BaseUrl"] = "http://api.interno:8080",
        });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.Null(exception);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} {Flatten(exception.InnerException)}";
}
