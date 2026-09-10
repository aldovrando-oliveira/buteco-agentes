using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Captura o SQL que o EF Core realmente emitiu durante uma requisição HTTP,
/// lendo o log de <c>Executed DbCommand</c> da categoria
/// <c>Microsoft.EntityFrameworkCore.Database.Command</c>.
///
/// Existe por causa da metade determinística dos guardas de ordenação
/// (design.md de ordenacao-desempate-listas-vinculo, D6, "Correção feita
/// durante a implementação"). O guarda comportamental de desempate afirma
/// "ordem crescente de id", e sem o <c>ThenBy</c> o PostgreSQL às vezes já
/// devolve nessa ordem sozinho — um index scan pela chave primária emite
/// exatamente assim. Medido: o guarda de <c>mcpServers</c> passou com o defeito
/// presente em 1 de 3 execuções da sua classe. "Ordenado por id porque o ThenBy
/// existe" e "ordenado por id porque o plano calhou" são a mesma observação, e
/// nenhum arranjo de teste as separa.
///
/// A asserção sobre o SQL emitido reprova no instante em que o <c>ThenBy</c>
/// sai, sempre. E é sobre a consulta **de produção**, emitida pelo caminho real
/// da requisição — nunca uma consulta remontada no teste com os mesmos
/// operadores, que passaria igual com o comportamento certo e com o errado
/// (convenção 11).
/// </summary>
public sealed class EmittedSqlCapture : ILoggerProvider
{
    public const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly ConcurrentQueue<string> _commands = new();

    /// <summary>
    /// Roda <paramref name="action"/> e devolve o SQL de todos os comandos que o
    /// EF Core executou durante ela. Os testes desta suíte rodam em sequência
    /// dentro de cada classe (xUnit), e cada classe tem a sua própria
    /// <see cref="ApiFactoryFixture"/> — logo o seu próprio host e o seu próprio
    /// sink —, então limpar antes de capturar é seguro.
    /// </summary>
    public async Task<IReadOnlyList<string>> CaptureAsync(Func<Task> action)
    {
        _commands.Clear();
        await action();
        return [.. _commands];
    }

    /// <summary>
    /// O único comando capturado que contém todos os fragmentos informados.
    /// Falha com mensagem útil quando nenhum casa (a consulta procurada não foi
    /// emitida — asserção que passaria em silêncio se devolvesse vazio) ou
    /// quando mais de um casa (o teste estaria afirmando sobre a consulta
    /// errada).
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

        var rendered = commands.Count == 0
            ? "(nenhum comando capturado)"
            : string.Join("\n---\n", commands);

        throw new InvalidOperationException(
            $"Esperado exatamente 1 comando contendo [{string.Join(", ", fragments)}], encontrados {matches.Count}. " +
            $"Comandos capturados:\n{rendered}");
    }

    /// <summary>
    /// Afirma que o <c>ORDER BY</c> do comando termina no desempate por
    /// identificador. Verificado contra os três formatos reais que o EF Core
    /// emite hoje — <c>ORDER BY m."Name"</c> (sem desempate) e
    /// <c>ORDER BY k."Name", k."Id"</c> (com) —, e insensível ao alias que o
    /// provider escolher para a tabela.
    /// </summary>
    public static void AssertOrderByEndsWithTieBreak(string command, string tieBreakColumn = "\"Id\"")
    {
        var orderByIndex = command.IndexOf("ORDER BY", StringComparison.Ordinal);
        Assert.True(orderByIndex >= 0, $"O comando não tem ORDER BY nenhum:\n{command}");

        var orderByClause = command[orderByIndex..].Split('\n')[0].Trim();
        var terms = orderByClause["ORDER BY".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.True(
            terms.Length >= 2,
            $"ORDER BY tem um único critério, sem desempate — é o defeito que este guarda existe para pegar:\n{orderByClause}");
        Assert.True(
            terms[^1].Contains(tieBreakColumn, StringComparison.Ordinal),
            $"O último critério do ORDER BY não é o desempate por {tieBreakColumn}:\n{orderByClause}");
    }

    ILogger ILoggerProvider.CreateLogger(string categoryName) =>
        categoryName == EfCommandCategory ? new SinkLogger(_commands) : NullLogger.Instance;

    void IDisposable.Dispose()
    {
    }

    private sealed class SinkLogger(ConcurrentQueue<string> commands) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                commands.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
