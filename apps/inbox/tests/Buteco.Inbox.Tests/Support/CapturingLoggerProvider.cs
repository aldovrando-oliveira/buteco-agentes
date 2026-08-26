using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Inbox.Tests.Support;

// Mesmo mecanismo de CapturingLoggerProvider/CapturingLogger em
// apps/workers/tests/Buteco.Workers.Tests/TimeZoneStartupValidationTests.cs
// (convenção 7: duplicado localmente, não compartilhado entre projetos de
// teste) — adaptado para capturar LogLevel e a Exception original, não só
// o texto formatado, porque os testes de inbox-sweep-service-resiliencia
// precisam afirmar nível Error e a exceção capturada, não só a mensagem.
//
// Filtra por categoria (só DebounceSweepService, por padrão) — sem isso,
// captura também o log verboso de comando SQL do EF Core para TODO
// teste da classe (a fixture, e portanto este provider, é compartilhada
// por todos os Facts), crescendo sem limite durante a vida inteira da
// fixture e adicionando overhead mensurável o bastante pra tornar
// PollUntil de outros testes (não relacionados a esta change) flaky sob
// carga — achado ao rodar a suíte completa (9 classes concorrentes) e
// comparar contra a baseline sem este provider.
public sealed class CapturingLoggerProvider(List<CapturedLogEntry> entries, params string[] categoryPrefixes) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        Array.Exists(categoryPrefixes, prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
            ? new CapturingLogger(entries)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
    }
}

public sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);
