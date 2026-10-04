using Buteco.Connectors.Options;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Checagem de boot de <c>Api:BaseUrl</c> (design.md da change ciclo-de-sincronizacao,
/// D2; convenção 8). Ausente e vazio sobem, com aviso; presente e inválido derruba o
/// boot sem ecoar o valor. Molde de <c>ConnectorsConfigurationStartupValidationTests</c>
/// do <c>apps/api</c>, duplicado.
/// </summary>
public class ApiConfigurationValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithoutBaseUrl_DoesNotThrowAndWarns(string? baseUrl)
    {
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(baseUrl, logs);

        var exception = Record.Exception(host.ValidateApiConfiguration);

        Assert.Null(exception);
        Assert.Contains(logs.Lines, line =>
            line.StartsWith("Warning", StringComparison.Ordinal) &&
            line.Contains("Api:BaseUrl", StringComparison.Ordinal) &&
            line.Contains(SyncCodes.NotConfigured, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://localhost:5017")]
    [InlineData("https://api.interno")]
    public void Validate_WithAbsoluteHttpBaseUrl_DoesNotThrowNorWarn(string baseUrl)
    {
        var logs = new CapturingLoggerProvider();
        using var host = BuildHost(baseUrl, logs);

        var exception = Record.Exception(host.ValidateApiConfiguration);

        Assert.Null(exception);
        Assert.DoesNotContain(logs.Lines, line =>
            line.StartsWith("Warning", StringComparison.Ordinal) || line.StartsWith("Error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("api:8080")]
    [InlineData("ftp://api")]
    [InlineData("/caminho-relativo")]
    [InlineData("api-segredo-que-nao-pode-vazar")]
    public void Validate_WithInvalidBaseUrl_ThrowsNamingTheKeyWithoutEchoingTheValue(string baseUrl)
    {
        using var host = BuildHost(baseUrl, new CapturingLoggerProvider());

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateApiConfiguration);

        Assert.Contains("Api:BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(baseUrl, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// O guarda sobre a COMPOSIÇÃO REAL: remover a chamada do <c>Program.cs</c> faz este
    /// teste reprovar, mesmo com os de cima verdes.
    /// </summary>
    [Fact]
    public void RealComposition_WithInvalidBaseUrl_FailsToStart()
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = "ftp://api" });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("Api:BaseUrl", Flatten(exception), StringComparison.Ordinal);
    }

    // Sem o endereço, a navegação continua: o ciclo é opcional (D2).
    [Fact]
    public async Task RealComposition_WithoutBaseUrl_StartsWarnsAndServesNavigation()
    {
        await using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = "" });
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.GetAsync("/connectors/providers");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.Logs.Lines, line =>
            line.StartsWith("Warning", StringComparison.Ordinal) && line.Contains("Api:BaseUrl", StringComparison.Ordinal));
    }

    /// <summary>
    /// O factory sobe em <c>Development</c>, e o <c>appsettings.Development.json</c> traz
    /// <c>Api:BaseUrl</c> (D2). Sem o vazio fixado pelo factory, toda a suíte ligaria o
    /// ciclo contra <c>localhost:5017</c>. Prova que o vazio posto por <c>UseSetting</c>
    /// vence o arquivo.
    /// </summary>
    [Fact]
    public void DefaultFactory_ResolvesApiBaseUrlAsNotConfigured()
    {
        using var factory = new ConnectorsFactory();

        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ApiOptions>>().Value;

        Assert.False(options.IsConfigured, $"Api:BaseUrl resolvido como '{options.BaseUrl}' na composição padrão do factory.");
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {Flatten(exception.InnerException)}";

    private static IHost BuildHost(string? baseUrl, CapturingLoggerProvider logs)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Configure<ApiOptions>(options => options.BaseUrl = baseUrl);
        builder.Services.AddLogging(logging => logging.AddProvider(logs));
        return builder.Build();
    }
}
