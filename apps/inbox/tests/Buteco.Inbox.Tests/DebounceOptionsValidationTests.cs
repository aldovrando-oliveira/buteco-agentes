using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Checagem de boot de <c>Debounce:SessionSerializationMaxWait</c> (change
/// inbox-serializacao-por-sessao, D3; convenção 8). Os dois casos que reprovam
/// desligariam a serialização em silêncio: teto menor ou igual a zero, e teto menor
/// ou igual a <c>Debounce:Window</c>.
///
/// <para>
/// O host é construído de verdade, com <c>Configure&lt;DebounceOptions&gt;</c>, para
/// que o teste passe pela resolução por DI, como o <c>Program.cs</c>. O registro da
/// chamada no <c>Program.cs</c> é provado à parte, por
/// <see cref="InvalidMaxWait_FailsTheRealApplicationStartup"/>.
/// </para>
/// </summary>
public class DebounceOptionsValidationTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void NonPositiveMaxWait_FailsTheBoot_AndNamesKeyAndValue(string maxWait)
    {
        using var host = BuildHost(Window, TimeSpan.Parse(maxWait));

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateDebounceSessionSerializationMaxWait);

        Assert.Contains("Debounce:SessionSerializationMaxWait", exception.Message, StringComparison.Ordinal);
        Assert.Contains(TimeSpan.Parse(maxWait).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:10")]
    [InlineData("00:00:05")]
    public void MaxWaitNotAboveWindow_FailsTheBoot_AndNamesKeyValueAndWindow(string maxWait)
    {
        using var host = BuildHost(Window, TimeSpan.Parse(maxWait));

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateDebounceSessionSerializationMaxWait);

        Assert.Contains("Debounce:SessionSerializationMaxWait", exception.Message, StringComparison.Ordinal);
        Assert.Contains(TimeSpan.Parse(maxWait).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Debounce:Window", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Window.ToString(), exception.Message, StringComparison.Ordinal);
    }

    // O par. Sem ele, a checagem poderia reprovar sempre e os casos acima ficariam
    // verdes do mesmo jeito. A linha de sucesso traz o teto conferido: sem ela, uma
    // checagem que passa é indistinguível de uma que não rodou.
    [Fact]
    public void MaxWaitAboveWindow_Boots_AndLogsTheCheckedValue()
    {
        var entries = new List<CapturedLogEntry>();
        var maxWait = TimeSpan.FromSeconds(11);
        using var host = BuildHost(Window, maxWait, entries);

        host.ValidateDebounceSessionSerializationMaxWait();

        var line = Assert.Single(entries, e => e.Level == LogLevel.Information);
        Assert.Contains(maxWait.ToString(), line.Message, StringComparison.Ordinal);
    }

    // O default é o valor de produção quando ninguém configura nada: ele tem de passar
    // pela própria checagem contra o Window default.
    [Fact]
    public void TheDefaults_Boot()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<DebounceOptions>(_ => { });
        using var host = builder.Build();

        host.ValidateDebounceSessionSerializationMaxWait();
    }

    /// <summary>
    /// Prova o <b>registro</b> da chamada no <c>Program.cs</c>: a aplicação real,
    /// pela <see cref="WebApplicationFactory{TEntryPoint}"/>, não sobe com teto zero.
    /// Sem Postgres: a checagem roda antes de qualquer acesso ao banco.
    /// </summary>
    [Fact]
    public void InvalidMaxWait_FailsTheRealApplicationStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseIssuer().ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                ["Api:BaseUrl"] = "http://apps-api.test",
                ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
                ["Debounce:SessionSerializationMaxWait"] = "00:00:00",
            }).AddInMemoryCollection(TestAuthentication.ConfigOverrides)));

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Debounce:SessionSerializationMaxWait", Flatten(exception), StringComparison.Ordinal);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Flatten(exception.InnerException)}";

    private static IHost BuildHost(TimeSpan window, TimeSpan maxWait, List<CapturedLogEntry>? entries = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<DebounceOptions>(options =>
        {
            options.Window = window;
            options.SessionSerializationMaxWait = maxWait;
        });
        if (entries is not null)
        {
            builder.Logging.AddProvider(new CapturingLoggerProvider(entries, typeof(DebounceOptionsValidation).FullName!));
        }

        return builder.Build();
    }
}
