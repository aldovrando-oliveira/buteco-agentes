using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Buteco.Connectors.Tests.Support;

// Mesmo mecanismo de apps/inbox/tests/.../CapturingLoggerProvider.cs (duplicado
// localmente), mas capturando TODAS as categorias: a busca da chave na saída
// (SecretLeakTests) precisa ver o log do framework e o do HttpClient, não só o do app.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
            if (exception is not null)
            {
                lines.Enqueue(exception.ToString());
            }
        }
    }
}
