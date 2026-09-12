using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Tests.Knowledge.Support;

/// <summary>
/// Captura o SQL que o EF Core realmente emitiu, lendo o log de
/// <c>Executed DbCommand</c> da categoria
/// <c>Microsoft.EntityFrameworkCore.Database.Command</c>.
///
/// <para>
/// <b>Duplicado de <c>Buteco.Api.Tests.Support.EmittedSqlCapture</c></b>, e não
/// promovido a <c>libs/</c>: os projetos de teste dos dois apps não se
/// referenciam, mesmo isolamento que obriga as entidades espelho e os três
/// <c>Null*Resolver</c>. Esta versão é menor — só o que a busca vetorial precisa.
/// </para>
///
/// <para>
/// <b>Por que existe (convenção 15, quinta forma):</b> o guarda comportamental
/// de ordenação afirma "os trechos vêm em distância crescente", e com poucas
/// linhas o PostgreSQL às vezes já devolve nessa ordem sem o <c>ORDER BY</c> —
/// um seq scan emite na ordem física, que é a ordem de inserção. *"Ordenado
/// porque o ORDER BY existe"* e *"ordenado porque o plano calhou"* são a mesma
/// observação, e nenhum arranjo de teste as separa. Medido no caso análogo de
/// <c>ordenacao-desempate-listas-vinculo</c>: o guarda passou com o defeito
/// presente em 1 de 3 execuções.
/// </para>
///
/// <para>
/// A asserção sobre o SQL emitido reprova sempre, e é sobre a consulta <b>de
/// produção</b> — emitida pela invocação real da tool, nunca uma consulta
/// remontada no teste com os mesmos operadores, que passaria igual com o
/// comportamento certo e com o errado (convenção 11).
/// </para>
/// </summary>
public sealed class EmittedSqlCapture : ILoggerProvider
{
    public const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly ConcurrentQueue<string> _commands = new();

    public async Task<IReadOnlyList<string>> CaptureAsync(Func<Task> action)
    {
        _commands.Clear();
        await action();
        return [.. _commands];
    }

    /// <summary>
    /// O único comando capturado que contém todos os fragmentos informados.
    /// Estoura quando nenhum casa (a consulta procurada não foi emitida —
    /// asserção que passaria em silêncio se devolvesse vazio) e quando mais de
    /// um casa (o teste estaria afirmando sobre a consulta errada).
    /// </summary>
    public static string SingleCommandContaining(IReadOnlyList<string> commands, params string[] fragments)
    {
        var matches = commands
            .Where(command => fragments.All(fragment => command.Contains(fragment, StringComparison.Ordinal)))
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var rendered = commands.Count == 0 ? "(nenhum comando capturado)" : string.Join("\n---\n", commands);
        throw new InvalidOperationException(
            $"Esperado exatamente 1 comando contendo [{string.Join(", ", fragments)}], encontrados {matches.Count}. "
          + $"Comandos capturados:\n{rendered}");
    }

    /// <summary>
    /// Afirma que a busca ordena pelo operador de distância do pgvector
    /// (<c>&lt;=&gt;</c>) e limita o número de linhas — as duas metades do
    /// "k mais próximos". Sem o <c>ORDER BY</c> o resultado é arbitrário; sem o
    /// <c>LIMIT</c> a tool devolveria o índice inteiro ao modelo.
    /// </summary>
    public static void AssertOrdersByVectorDistanceWithLimit(string command)
    {
        var orderByIndex = command.IndexOf("ORDER BY", StringComparison.Ordinal);
        Assert.True(orderByIndex >= 0, $"A busca não emitiu ORDER BY nenhum:\n{command}");

        var tail = command[orderByIndex..];
        Assert.True(
            tail.Contains("<=>", StringComparison.Ordinal),
            $"O ORDER BY não usa o operador de distância de cosseno do pgvector:\n{tail}");
        Assert.True(
            command.Contains("LIMIT", StringComparison.Ordinal),
            $"A busca não emitiu LIMIT — devolveria o índice inteiro:\n{command}");
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) =>
        categoryName == EfCommandCategory ? new SinkLogger(_commands) : NullSink.Instance;

    void IDisposable.Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<string> commands) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                commands.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class NullSink : ILogger
    {
        public static readonly NullSink Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
