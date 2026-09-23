using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests;

/// <summary>
/// Guardas da checagem de fuso de <c>apps/api</c> (convenção 8).
///
/// <para>
/// <b>O par completo:</b> nome válido sobe E se anuncia; nome que não resolve,
/// nome com prefixo POSIX e <c>TZ</c> ausente reprovam. O fuso é fixado por
/// <see cref="TimeZoneInfo.CreateCustomTimeZone"/>, e não lido da máquina, pelo
/// mesmo motivo que os testes gêmeos de <c>apps/workers</c>: um teste que
/// dependa da tz database da máquina testa a máquina, não o código.
/// </para>
///
/// <para>
/// <b>E o último teste é o que <c>apps/workers</c> NÃO consegue ter.</b> Lá o
/// registro do <c>Program.cs</c> não é provado por teste nenhum, porque a suíte
/// monta o host à mão. Aqui a <see cref="WebApplicationFactory{TEntryPoint}"/>
/// roda a composição REAL: remover a chamada do <c>Program.cs</c> faz
/// <see cref="RealComposition_WithDivergentTimeZone_FailsToStart"/> reprovar.
/// </para>
/// </summary>
public class TimeZoneStartupValidationTests
{
    // Id realista, mas construído — não depende da tz database da máquina.
    private const string ZoneId = "America/Sao_Paulo";

    private static readonly TimeZoneInfo FixedZone =
        TimeZoneInfo.CreateCustomTimeZone(ZoneId, TimeSpan.FromHours(-3), "-03:00", "-03:00");

    [Fact]
    public void Validate_WithDeclaredMatchingResolved_DoesNotThrowAndAnnouncesItself()
    {
        var logs = new List<string>();
        using var host = BuildHost(ZoneId, logs);

        var exception = Record.Exception(host.ValidateTimeZoneConfiguration);

        Assert.Null(exception);

        // O par positivo não é só "não lançou": a linha de anúncio é a ÚNICA
        // prova, em produção, de que a checagem rodou. Sem esta asserção, uma
        // checagem que passasse em silêncio seria indistinguível de uma que não
        // existisse.
        Assert.Contains(logs, line => line.Contains(ZoneId, StringComparison.Ordinal));
    }

    [Theory]
    // Nome que não resolve — o .NET cai para outro fuso e os identificadores
    // divergem. É por isso que NÃO existe um segundo guarda "o nome resolve":
    // esta mesma comparação já o cobre.
    [InlineData("Amrica/Sao_Paulo")]
    // Prefixo POSIX ":" — o .NET resolve o fuso corretamente COM o prefixo, mas
    // o remove do identificador que expõe. A configuração parece certa e a
    // comparação reprova, que é exatamente o caso que o texto do erro nomeia.
    [InlineData(":America/Sao_Paulo")]
    // Vazia.
    [InlineData("")]
    public void Validate_WithDeclaredDivergingFromResolved_Throws(string declared)
    {
        using var host = BuildHost(declared, []);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateTimeZoneConfiguration);

        Assert.Contains("TZ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ZoneId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WithoutDeclaredTimeZone_Throws()
    {
        using var host = BuildHost(declaredTimeZone: null, []);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateTimeZoneConfiguration);

        Assert.Contains(ZoneId, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// O guarda sobre a COMPOSIÇÃO REAL. Ele não exercita a extensão — exercita
    /// o <c>Program.cs</c>: um <c>TZ</c> declarado que não corresponde ao fuso da
    /// máquina tem de derrubar a subida da aplicação de verdade.
    ///
    /// <para>
    /// <b>É o teste que reprova se alguém remover a chamada</b>, e é o que a
    /// suíte de <c>apps/workers</c> não tem. Sem ele, os guardas acima ficariam
    /// verdes com a produção subindo sem checagem nenhuma.
    /// </para>
    /// </summary>
    [Fact]
    public void RealComposition_WithDivergentTimeZone_FailsToStart()
    {
        using var factory = new DivergentTimeZoneFactory();

        // Resolver Services é o que constrói o host e roda o Program.cs até o
        // ponto em que o servidor subiria.
        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains(
            "Fuso horário não configurado corretamente",
            Flatten(exception),
            StringComparison.Ordinal);
    }

    private sealed class DivergentTimeZoneFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);

                // Depois do bloco acima, de propósito: sobrescreve o TZ que as
                // fixtures usam por um valor que NÃO corresponde ao fuso da
                // máquina, qualquer que ela seja.
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TZ"] = "Antarctica/Troll",
                });
            });
        }
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {Flatten(exception.InnerException)}";

    private static IHost BuildHost(string? declaredTimeZone, List<string> logs)
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(FixedZone);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        builder.Services.Configure<MetricsOptions>(options => options.TimeZone = declaredTimeZone);
        builder.Services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)));

        return builder.Build();
    }

    // Duplo de teste — contado como duplo e não como caso (convenção 18), ainda
    // que more dentro do arquivo de casos. apps/api não tinha captura de log;
    // apps/workers tem a sua (CapturingLoggerProvider), e extrair uma terceira
    // cópia para libs/ seria abstração antes do consumidor (convenção 2).
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
