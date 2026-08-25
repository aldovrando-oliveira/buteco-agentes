using Buteco.Workers.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-contexto-temporal, Seção 7 do tasks.md —
/// checagem de boot de <c>TZ</c>. NÃO usa <c>WorkerInfrastructureFixture</c>/
/// <c>BuildHost</c>: constrói um host mínimo próprio, registrando só
/// <see cref="TimeProvider.System"/> — sem Postgres, sem RabbitMQ, sem
/// <c>AgentExecutionService</c> (design.md, Decisão 9). Mecanismo de
/// mutação: <see cref="Environment.SetEnvironmentVariable(string, string?)"/>
/// seguido de <see cref="TimeZoneInfo.ClearCachedData"/>, confirmado real e
/// repetível em container Linux e nativamente em macOS (design.md, Achado 7)
/// — não subprocesso (Decisão 9, alternativa rejeitada).
///
/// Estes três testes mutam estado global do processo (variável de ambiente
/// <c>TZ</c>, cache de <see cref="TimeZoneInfo"/>) e por isso ficam juntos
/// nesta classe — xUnit roda testes de uma mesma classe em sequência entre
/// si por padrão, evitando que eles se atropelem. Cada um restaura o valor
/// original de <c>TZ</c> e chama <see cref="TimeZoneInfo.ClearCachedData"/>
/// de novo ao final, para não vazar estado mutado a outros testes do mesmo
/// processo.
///
/// Invariante que torna seguro rodar esta classe em paralelo com o resto da
/// suíte (design.md, Decisão 9, Decisões A e B — tripwire, não garantia):
/// depois da Tarefa 1.3 (<c>TaskJobConsumer</c> passou a usar o
/// <c>TimeProvider</c> injetado em vez de <c>DateTimeOffset.Now</c> direto),
/// nenhum código de <c>apps/workers</c> nem dos projetos de teste na raiz
/// do repo em <c>tests/</c> — produção ou teste — lê
/// <see cref="TimeZoneInfo.Local"/>/<see cref="TimeProvider.System"/> reais,
/// verificado por varredura completa de <c>apps/workers</c> (design.md,
/// Achado 9), estendida depois a <c>tests/</c> na raiz do repo (design.md,
/// Achado 10) — não por suposição, e não uma varredura de todo o código
/// do repo (código de produção de <c>apps/api</c>/<c>apps/inbox</c> nunca
/// foi varrido por este eixo, nem precisa ser). O resíduo fora do alcance
/// dessas duas varreduras é código de framework (logging, Npgsql/EF Core,
/// host) — categoria diferente, sem contraparte de código deste repo para
/// corrigir, e nenhuma asserção da suíte depende de um valor derivado de
/// fuso local. Se aparecer flake intermitente na suíte de integração de
/// <c>apps/workers</c> ou nos projetos de teste em <c>tests/</c>, este
/// parágrafo e os Achados 9/10 são o primeiro lugar a olhar.
/// </summary>
public class TimeZoneStartupValidationTests
{
    [Fact]
    public void ValidateTimeZoneConfiguration_TzNotSet_Throws()
    {
        WithTimeZoneEnvironmentVariable(declaredValue: null, () =>
        {
            using var host = BuildMinimalHost(out _);

            var exception = Assert.Throws<InvalidOperationException>(() => host.ValidateTimeZoneConfiguration());
            Assert.Contains("TZ=", exception.Message);
        });
    }

    [Fact]
    public void ValidateTimeZoneConfiguration_TzInvalid_Throws()
    {
        // Typo verificado em container Linux real (design.md, Achado 5) —
        // resolve para UTC de forma silenciosa, sem exceção do runtime;
        // é exatamente o caso que a comparação contra o resultado (não só
        // a presença da variável) existe para pegar.
        WithTimeZoneEnvironmentVariable(declaredValue: "America/Sao_Paolo", () =>
        {
            using var host = BuildMinimalHost(out _);

            var exception = Assert.Throws<InvalidOperationException>(() => host.ValidateTimeZoneConfiguration());
            Assert.Contains("America/Sao_Paolo", exception.Message);
        });
    }

    [Fact]
    public void ValidateTimeZoneConfiguration_TzValid_DoesNotThrowAndLogsResolvedZoneAndOffset()
    {
        // TZ=UTC resolve TimeZoneInfo.Local.Id="UTC" em qualquer plataforma
        // testada (design.md, Achado 5 — container Linux; verificado também
        // nativamente em macOS antes deste teste) — bate exatamente com o
        // valor declarado, sem caso especial no código para UTC.
        WithTimeZoneEnvironmentVariable(declaredValue: "UTC", () =>
        {
            using var host = BuildMinimalHost(out var capturedLogs);

            var exception = Record.Exception(() => host.ValidateTimeZoneConfiguration());

            Assert.Null(exception);
            Assert.Contains(
                capturedLogs,
                message => message.Contains("Fuso horário do sistema", StringComparison.Ordinal)
                    && message.Contains("UTC", StringComparison.Ordinal));
        });
    }

    private static void WithTimeZoneEnvironmentVariable(string? declaredValue, Action test)
    {
        var originalValue = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", declaredValue);
            TimeZoneInfo.ClearCachedData();

            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalValue);
            TimeZoneInfo.ClearCachedData();
        }
    }

    private static IHost BuildMinimalHost(out List<string> capturedLogs)
    {
        var logs = new List<string>();
        capturedLogs = logs;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CapturingLoggerProvider(logs));

        return builder.Build();
    }

    private sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }
}
