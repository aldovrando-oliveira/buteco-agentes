using Buteco.Api.KnowledgeSync.Connectors;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests;

/// <summary>
/// Checagem de boot de <c>Connectors:BaseUrl</c> (design.md da change
/// criacao-base-sincronizada, D1; convenção 8). Ausente e vazio sobem, com aviso;
/// presente e inválido derruba o boot sem ecoar o valor. Sem contêiner: o último caso
/// sobe a composição real, mas falha antes de qualquer acesso ao banco.
/// </summary>
public class ConnectorsConfigurationStartupValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithoutBaseUrl_DoesNotThrowAndWarns(string? baseUrl)
    {
        var logs = new List<(LogLevel Level, string Message)>();
        using var host = BuildHost(baseUrl, logs);

        var exception = Record.Exception(host.ValidateConnectorsConfiguration);

        Assert.Null(exception);
        Assert.Contains(logs, line =>
            line.Level == LogLevel.Warning &&
            line.Message.Contains("Connectors:BaseUrl", StringComparison.Ordinal) &&
            line.Message.Contains(ConnectorsFailureCodes.NotConfigured, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://localhost:5037")]
    [InlineData("https://connectors.interno")]
    public void Validate_WithAbsoluteHttpBaseUrl_DoesNotThrowNorWarn(string baseUrl)
    {
        var logs = new List<(LogLevel Level, string Message)>();
        using var host = BuildHost(baseUrl, logs);

        var exception = Record.Exception(host.ValidateConnectorsConfiguration);

        Assert.Null(exception);
        Assert.DoesNotContain(logs, line => line.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("connectors:8080")]
    [InlineData("ftp://connectors")]
    [InlineData("/caminho-relativo")]
    [InlineData("conectores-segredo-que-nao-pode-vazar")]
    public void Validate_WithInvalidBaseUrl_ThrowsNamingTheKeyWithoutEchoingTheValue(string baseUrl)
    {
        using var host = BuildHost(baseUrl, []);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateConnectorsConfiguration);

        Assert.Contains("Connectors:BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(baseUrl, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// O guarda sobre a COMPOSIÇÃO REAL: remover a chamada do <c>Program.cs</c> faz este
    /// teste reprovar, mesmo com os de cima verdes.
    /// </summary>
    [Fact]
    public void RealComposition_WithInvalidBaseUrl_FailsToStart()
    {
        using var factory = new InvalidBaseUrlFactory();

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("Connectors:BaseUrl", Flatten(exception), StringComparison.Ordinal);
    }

    private sealed class InvalidBaseUrlFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Connectors:BaseUrl"] = "ftp://connectors" });
            });
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {Flatten(exception.InnerException)}";

    private static IHost BuildHost(string? baseUrl, List<(LogLevel Level, string Message)> logs)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.Configure<ConnectorsOptions>(options => options.BaseUrl = baseUrl);
        builder.Services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)));
        return builder.Build();
    }

    // Duplo de teste, como os de TimeZoneStartupValidationTests: a captura daqui
    // precisa do nível, e a de lá só guarda o texto.
    private sealed class ListLoggerProvider(List<(LogLevel Level, string Message)> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<(LogLevel Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
