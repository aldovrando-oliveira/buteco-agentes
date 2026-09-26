using Buteco.Api.Infrastructure;
using Buteco.Api.RejectionMetrics;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests;

/// <summary>
/// <b>O escritor da métrica de recusa não lança</b> (convenção 4, design.md D8):
/// métrica que muda o resultado do que ela mede não é métrica.
///
/// <para>
/// <b>Sem contêiner, de propósito.</b> O caso precisa de um banco INALCANÇÁVEL, e
/// um banco inalcançável não precisa existir — a connection string aponta para uma
/// porta onde ninguém escuta. É o único guarda desta change que precisa de duplo, e
/// o duplo é exatamente este: o <c>AppDbContext</c> apontado para o vazio.
/// </para>
///
/// <para>
/// <b>O par positivo NÃO mora aqui</b>, e sim nos quatro casos de recusa contra o
/// Postgres real (<c>SendMessageProviderRejectionTests</c>,
/// <c>AgentDeactivationTests</c>): um escritor exercitado só contra banco
/// inalcançável passaria verde sem nunca ter gravado nada.
/// </para>
/// </summary>
public class RejectionMetricsWriterTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_WithUnreachableDatabase_DoesNotThrow_AndWarnsWithTheTaskId()
    {
        var logs = new List<string>();
        await using var provider = BuildProvider(logs);
        var writer = provider.GetRequiredService<RejectionMetricsWriter>();

        var exception = await Record.ExceptionAsync(() => writer.WriteAsync(
            "task-que-nao-vai-ser-gravada",
            Guid.NewGuid(),
            RejectionMetricsValues.Reason.AgentInactive,
            CancellationToken.None));

        Assert.Null(exception);

        // O aviso é a única prova de que a falha não passou em silêncio — e ele
        // carrega o TaskId, que é o que liga a métrica perdida à task real.
        Assert.Contains(
            logs,
            line => line.Contains("task-que-nao-vai-ser-gravada", StringComparison.Ordinal)
                && line.Contains("métrica de recusa", StringComparison.Ordinal));
    }

    /// <summary>
    /// O cancelamento também NÃO escapa: o escritor roda depois de a task já estar
    /// recusada, então um token já cancelado — desligamento, cliente desconectado —
    /// não pode virar exceção no caminho de recusa.
    /// </summary>
    [Fact]
    public async Task WriteAsync_WithAlreadyCancelledToken_DoesNotThrow()
    {
        await using var provider = BuildProvider([]);
        var writer = provider.GetRequiredService<RejectionMetricsWriter>();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exception = await Record.ExceptionAsync(() => writer.WriteAsync(
            "task-cancelada", Guid.NewGuid(), RejectionMetricsValues.Reason.AgentNotFound, cancelled.Token));

        Assert.Null(exception);
    }

    private static ServiceProvider BuildProvider(List<string> logs)
    {
        var services = new ServiceCollection();

        // Porta onde ninguém escuta: o SaveChanges falha na conexão, que é o modo
        // de falha real que o guarda existe para cobrir.
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(
            "Host=127.0.0.1;Port=1;Database=buteco;Username=buteco;Password=buteco;Timeout=1;Command Timeout=1"));

        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Instant));
        services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs)));
        services.AddSingleton<RejectionMetricsWriter>();

        return services.BuildServiceProvider();
    }

    // TERCEIRA cópia da captura de log em apps/api (as outras duas estão em
    // TimeZoneStartupValidationTests e MetricsRegimeStartupValidationTests) — e é o
    // gatilho de extrair para Support/ que a convenção 2 pede: o terceiro
    // consumidor. Fica como item aberto desta change, não como refatoração dentro
    // dela: mexer nas duas classes existentes alargaria o diff para além do escopo.
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
