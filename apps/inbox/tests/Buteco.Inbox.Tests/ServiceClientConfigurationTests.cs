using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Buteco.Inbox.Tests;

/// <summary>
/// O segredo do <c>apps/inbox</c> no emissor, conferido no boot da aplicação real (spec
/// <c>service-client-credentials</c>, "Segredo do serviço obrigatório e conferido no boot";
/// design.md da change servicos-token-emissor, D5; convenção 8): presente, base64, 32 bytes, e
/// distinto da <c>Inbox:CredentialEncryptionKey</c> (decisão do dono, 03/11/2026). Nenhuma
/// mensagem ecoa o valor. Sem banco: o boot falha antes de qualquer acesso.
/// </summary>
public class ServiceClientConfigurationTests
{
    private const string CredentialEncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";

    [Theory]
    [InlineData("")]
    [InlineData("isto-nao-e-base64-mas-e-longo-o-bastante!!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void AnInvalidSecret_FailsTheRealStartup_NamingTheSetting_WithoutTheValue(string secret)
    {
        using var factory = RealApplication(secret);

        var message = Flatten(Assert.ThrowsAny<Exception>(() => factory.Services));

        Assert.Contains(ServiceClientOptions.SecretConfigurationName, message, StringComparison.Ordinal);
        if (secret.Length > 0)
        {
            Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ASecretEqualToTheCredentialEncryptionKey_FailsTheRealStartup_NamingBothSettings()
    {
        using var factory = RealApplication(CredentialEncryptionKey);

        var message = Flatten(Assert.ThrowsAny<Exception>(() => factory.Services));

        Assert.Contains(ServiceClientOptions.SecretConfigurationName, message, StringComparison.Ordinal);
        Assert.Contains("Inbox:CredentialEncryptionKey", message, StringComparison.Ordinal);
        Assert.DoesNotContain(CredentialEncryptionKey, message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValidSecret_Boots()
    {
        using var factory = RealApplication(TestAuthentication.ServiceClientSecret);

        Assert.NotNull(factory.Services);
    }

    private static WebApplicationFactory<Program> RealApplication(string secret) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseIssuer(TestIssuer.Issuer).ConfigureAppConfiguration((_, config) => config
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Inbox:CredentialEncryptionKey"] = CredentialEncryptionKey,
                    ["Api:BaseUrl"] = "http://apps-api.test",
                    ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
                })
                .AddInMemoryCollection(TestAuthentication.ConfigOverrides)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceClient:Secret"] = secret })));

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Flatten(exception.InnerException)}";
}
