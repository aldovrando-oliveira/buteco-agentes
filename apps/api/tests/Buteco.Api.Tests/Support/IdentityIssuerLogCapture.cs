using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Captura os logs do emissor OIDC e dos usuários locais (categorias
/// <c>Buteco.Api.Identity.*</c> e <c>Buteco.Api.Users.*</c>) de um host
/// de teste inteiro, de qualquer thread: o aviso de boot do emissor desligado e as
/// falhas do aquecimento, que acontecem num <c>BackgroundService</c>.
/// </summary>
public sealed class IdentityIssuerLogCapture : ILoggerProvider
{
    private static readonly string[] CategoryPrefixes = ["Buteco.Api.Identity", "Buteco.Api.Users"];

    private readonly ConcurrentQueue<(LogLevel Level, EventId EventId, string Message)> _lines = new();

    public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Lines => [.. _lines];

    ILogger ILoggerProvider.CreateLogger(string categoryName) =>
        CategoryPrefixes.Any(prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
            ? new SinkLogger(_lines)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    void IDisposable.Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<(LogLevel Level, EventId EventId, string Message)> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => lines.Enqueue((logLevel, eventId, formatter(state, exception)));
    }
}
