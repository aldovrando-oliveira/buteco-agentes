using System.Text;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// A checagem de boot de <c>Identity:FederatedProviders</c> (spec <c>federated-providers</c>, "Checagem
/// de boot da seção, com lista fechada"; design.md da change provedores-federados-google, D3), sempre
/// sobre a composição real do <c>Program.cs</c>, sem contêiner: todas falham antes do banco.
///
/// <para>
/// O segredo "pela variável de ambiente" entra aqui por uma coleção em memória, que, como a variável,
/// não é arquivo JSON; o "pelo arquivo" entra por um <c>AddJsonStream</c> depois dela, que passa a ser a
/// fonte efetiva. Guardas verificados contra o defeito (tasks.md, 1.5 a 1.7).
/// </para>
/// </summary>
public class FederatedProvidersStartupValidationTests
{
    public const string Secret = "GOCSPX-segredo-do-teste-federado";

    public static Dictionary<string, string?> Google(string key = "corporativo") => new()
    {
        [$"Identity:FederatedProviders:{key}:Type"] = "google",
        [$"Identity:FederatedProviders:{key}:ClientId"] = "1234567890-abc.apps.googleusercontent.com",
        [$"Identity:FederatedProviders:{key}:ClientSecret"] = Secret,
    };

    [Fact]
    public void WithoutTheSection_Boots()
    {
        Assert.Null(BootFailure(new()));
    }

    [Fact]
    public void WithOneGoogleProvider_SecretFromTheEnvironment_Boots()
    {
        // "O mesmo segredo pela variável passa": a mesma entrada do caso do JSON, abaixo, sem o arquivo.
        Assert.Null(BootFailure(Google()));
    }

    [Fact]
    public void WithTheSecretInAJsonFile_FailsNamingTheSetting_WithoutTheValue()
    {
        var settings = Google();
        settings.Remove("Identity:FederatedProviders:corporativo:ClientSecret");
        var json = $$"""{ "Identity": { "FederatedProviders": { "corporativo": { "ClientSecret": "{{Secret}}" } } } }""";

        var message = BootFailure(settings, json);

        Assert.NotNull(message);
        Assert.Contains("Identity:FederatedProviders:corporativo:ClientSecret veio de um arquivo de configuração", message, StringComparison.Ordinal);
        Assert.Contains("variável de ambiente", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTwoGoogleProviders_FailsNamingBothAndTheIssuer()
    {
        var settings = Google("primeiro");
        foreach (var (key, value) in Google("segundo"))
        {
            settings[key] = value + (key.EndsWith("ClientSecret", StringComparison.Ordinal) ? "-segundo" : "");
        }

        var message = BootFailure(settings);

        Assert.NotNull(message);
        Assert.Contains("'primeiro' e 'segundo' usam o mesmo issuer (https://accounts.google.com/)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithAMistypedField_FailsNamingTheField()
    {
        var settings = Google();
        settings.Remove("Identity:FederatedProviders:corporativo:ClientId");
        settings["Identity:FederatedProviders:corporativo:ClientID"] = "1234567890-abc.apps.googleusercontent.com";

        var message = BootFailure(settings);

        Assert.NotNull(message);
        Assert.Contains("Identity:FederatedProviders:corporativo:ClientID desconhecido", message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string?, string> EachRule => new()
    {
        // (campo a mudar, valor novo ou nulo para retirar, trecho da mensagem)
        { "@chave", "Corporativo", "Identity:FederatedProviders:Corporativo: a chave precisa ter de 2 a 32 caracteres" },
        { "Type", null, "Identity:FederatedProviders:corporativo:Type não configurado" },
        { "Type", "keycloak", "Identity:FederatedProviders:corporativo:Type 'keycloak' desconhecido: os tipos aceitos são google." },
        { "ClientId", " ", "Identity:FederatedProviders:corporativo:ClientId não configurado" },
        { "ClientSecret", null, "defina a variável Identity__FederatedProviders__corporativo__ClientSecret" },
        { "HostedDomain", "maria@empresa.com", "Identity:FederatedProviders:corporativo:HostedDomain 'maria@empresa.com' inválido" },
        { "HostedDomain", "https://empresa.com", "HostedDomain 'https://empresa.com' inválido" },
    };

    [Theory]
    [MemberData(nameof(EachRule))]
    public void EachInconsistency_FailsWithItsOwnMessage(string field, string? value, string expected)
    {
        var settings = Google();
        if (field == "@chave")
        {
            settings = Google(value!);
        }
        else if (value is null)
        {
            settings.Remove($"Identity:FederatedProviders:corporativo:{field}");
        }
        else
        {
            settings[$"Identity:FederatedProviders:corporativo:{field}"] = value;
        }

        var message = BootFailure(settings);

        Assert.NotNull(message);
        Assert.Contains(expected, message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheSecretEqualToAnOwnKey_FailsNamingIt_WithoutTheValue()
    {
        var twoFactor = TestAuthentication.ConfigOverrides.Single(entry => entry.Key == "Identity:TwoFactorEncryptionKey").Value!;
        var settings = Google();
        settings["Identity:FederatedProviders:corporativo:ClientSecret"] = twoFactor;

        var message = BootFailure(settings);

        Assert.NotNull(message);
        Assert.Contains("Identity:FederatedProviders:corporativo:ClientSecret igual a Identity:TwoFactorEncryptionKey", message, StringComparison.Ordinal);
        Assert.DoesNotContain(twoFactor, message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithAValidHostedDomain_Boots()
    {
        var settings = Google();
        settings["Identity:FederatedProviders:corporativo:HostedDomain"] = "empresa.com";

        Assert.Null(BootFailure(settings));
    }

    [Fact]
    public void WithAProvider_BootsWithoutReachingTheProvider()
    {
        // "Provedor fora do ar não impede o boot": nenhum HttpClient é chamado no boot. Sem rede
        // externa no teste, um discovery feito no boot reprovaria por exceção de conexão.
        Assert.Null(BootFailure(Google()));
    }

    /// <summary>A mensagem da falha do boot, ou nulo se a composição real subiu.</summary>
    public static string? BootFailure(Dictionary<string, string?> settings, string? json = null)
    {
        using var factory = new Factory(settings, json);
        var exception = Record.Exception(() => _ = factory.Services);
        return exception is null ? null : Flatten(exception);
    }

    private sealed class Factory(Dictionary<string, string?> settings, string? json) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PublicUrl:BaseUrl", ApiFactoryFixture.PublicBaseUrl);
            foreach (var (key, value) in settings)
            {
                // Por UseSetting, que chega ao registro dos serviços: o catálogo dos provedores é lido
                // antes do Build(), como o PublicUrl:BaseUrl (ver ApiFactoryFixture).
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
                config.AddInMemoryCollection(settings);
                if (json is not null)
                {
                    config.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
                }
            });
        }
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {Flatten(exception.InnerException)}";
}
