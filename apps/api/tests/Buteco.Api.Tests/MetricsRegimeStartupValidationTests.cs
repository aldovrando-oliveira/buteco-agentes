using Buteco.Api.Insights;
using Buteco.Api.Options;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests;

/// <summary>
/// Guardas da checagem de regime de medição (convenção 8, change
/// <c>recusa-motivo-coleta</c>, D7).
///
/// <para>
/// <b>O que ela impede é um defeito SILENCIOSO, não um erro.</b> Regime declarado
/// por um grupo de métricas e ausente da configuração não faz nada falhar: ele
/// desliga o recorte daquele grupo, e o período anterior à coleta passa a
/// responder <c>0</c> em vez de ausência. É por isso que o par positivo afirma a
/// LINHA DE ANÚNCIO e não só a ausência de exceção — uma checagem que passasse em
/// silêncio seria indistinguível de uma que não existisse.
/// </para>
///
/// <para>
/// <b>Sem contêiner, de propósito:</b> a checagem lê configuração e nada mais, e a
/// composição real sobe até o ponto do servidor sem tocar o banco — o mesmo que os
/// guardas gêmeos de fuso fazem. `apps/api` está em 40 classes de contêiner
/// (medido em 25/09/2026), e uma classe que não precisa de um não vira a 41ª.
/// </para>
/// </summary>
public class MetricsRegimeStartupValidationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Validate_WithEveryDeclaredRegimeConfigured_DoesNotThrowAndAnnouncesEachOne()
    {
        var logs = new List<string>();
        using var host = BuildHost(MetricsOptions.DeclaredRegimes.ToDictionary(regime => regime, _ => Start), logs);

        var exception = Record.Exception(host.ValidateMetricsRegimeConfiguration);

        Assert.Null(exception);

        // Cada regime é anunciado com o seu instante. Um "medindo desde" único
        // mentiria sobre dois dos três, e é essa a razão de o campo ser um mapa.
        foreach (var regime in MetricsOptions.DeclaredRegimes)
        {
            Assert.Contains(logs, line => line.Contains(regime, StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(MetricsOptions.ExecutionRegime)]
    [InlineData(MetricsOptions.EmbeddingRegime)]
    [InlineData(MetricsOptions.RejectionRegime)]
    public void Validate_WithOneDeclaredRegimeMissing_ThrowsNamingIt(string missing)
    {
        var regimes = MetricsOptions.DeclaredRegimes
            .Where(regime => regime != missing)
            .ToDictionary(regime => regime, _ => Start);

        using var host = BuildHost(regimes, []);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateMetricsRegimeConfiguration);

        // O NOME do que falta, e não só "configuração incompleta": a mensagem é
        // lida por quem está subindo um ambiente novo, e é ela que diz qual chave
        // escrever.
        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WithNoRegimeConfigured_NamesAllThree()
    {
        using var host = BuildHost([], []);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateMetricsRegimeConfiguration);

        foreach (var regime in MetricsOptions.DeclaredRegimes)
        {
            Assert.Contains(regime, exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// O guarda sobre a COMPOSIÇÃO REAL, no molde do gêmeo de fuso: ele não
    /// exercita a extensão, exercita o <c>Program.cs</c>.
    ///
    /// <para>
    /// <b>Aqui ele afirma o ANÚNCIO, e não uma exceção</b>, e a diferença tem
    /// causa: não há como fazer o regime FALTAR na composição real, porque
    /// configuração é aditiva — uma fonte posterior sobrescreve valor, nunca
    /// remove chave, e o <c>appsettings.json</c> declara os três. O que prova a
    /// chamada é a linha que só existe se ela rodou: remover
    /// <c>app.ValidateMetricsRegimeConfiguration()</c> do <c>Program.cs</c> faz
    /// este teste reprovar.
    /// </para>
    /// </summary>
    [Fact]
    public void RealComposition_RunsTheCheck_AndAnnouncesTheRejectionRegime()
    {
        var logs = new List<string>();
        using var factory = new LogCapturingFactory(logs);

        // Resolver Services constrói o host e roda o Program.cs até o ponto em que
        // o servidor subiria — inclusive as checagens de boot.
        _ = factory.Services;

        Assert.Contains(
            logs,
            line => line.Contains("Regime de medição", StringComparison.Ordinal)
                && line.Contains(MetricsOptions.RejectionRegime, StringComparison.Ordinal));
    }

    private sealed class LogCapturingFactory(List<string> logs) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides));

            builder.ConfigureLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)));
        }
    }

    private static IHost BuildHost(Dictionary<string, DateTimeOffset> regimes, List<string> logs)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Services.Configure<MetricsOptions>(options =>
        {
            foreach (var (regime, start) in regimes)
            {
                options.Regimes[regime] = start;
            }
        });

        builder.Services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)));

        return builder.Build();
    }

    // Duplo de teste, contado como duplo e não como caso (convenção 18). É a
    // SEGUNDA cópia desta captura em apps/api — a primeira está em
    // TimeZoneStartupValidationTests —, e ela continua copiada de propósito: o
    // terceiro consumidor é o gatilho de extrair (convenção 2), e o dia em que ele
    // aparecer as duas vão para Support/.
    private sealed class ListLoggerProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Add(formatter(state, exception));
        }
    }
}
