using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Captura os logs de um host de teste inteiro, de qualquer thread: o aviso de boot do emissor desligado e as
/// falhas do aquecimento, que acontecem num <c>BackgroundService</c>, e as linhas dos fluxos.
///
/// <para>
/// As categorias são as do Buteco (<c>Buteco.Api.*</c>) e as da biblioteca do emissor e do cliente OIDC
/// (<c>OpenIddict.*</c>, servidor e cliente). Até a #274 eram só <c>Buteco.Api.Identity.*</c> e
/// <c>Buteco.Api.Users.*</c>, e por isso o guarda de log da #189 não via a linha
/// <c>OpenIddictClientDispatcher[6195]</c>, que gravava o <c>sub</c> e o e-mail do Google em <c>Information</c>
/// (issue #277). A captura não filtra nível: recebe o que o host deixa passar, que é a configuração real
/// (<c>appsettings.json</c>), e é isso que os guardas de log afirmam.
/// </para>
/// </summary>
public sealed class IdentityIssuerLogCapture : ILoggerProvider
{
    private static readonly string[] CategoryPrefixes = ["Buteco.Api", "OpenIddict"];

    private readonly ConcurrentQueue<(LogLevel Level, EventId EventId, string Message, string Category)> _lines = new();

    public IReadOnlyList<(LogLevel Level, EventId EventId, string Message, string Category)> Lines => [.. _lines];

    ILogger ILoggerProvider.CreateLogger(string categoryName) =>
        CategoryPrefixes.Any(prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
            ? new SinkLogger(_lines, categoryName)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    void IDisposable.Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<(LogLevel Level, EventId EventId, string Message, string Category)> lines, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => lines.Enqueue((logLevel, eventId, formatter(state, exception), category));
    }
}
