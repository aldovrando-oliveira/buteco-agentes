using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Checagem de boot de <c>Embedding:BatchSize</c>.
///
/// <para>
/// <b>Sem <c>WorkerInfrastructureFixture</c> e fora da coleção de hosts, de
/// propósito.</b> (A redação evita o nome exato do atributo: a contagem de
/// classes da coleção é feita por <c>grep</c>, e uma menção em comentário
/// entraria nela como se fosse registro.) A checagem
/// não toca banco nem fila, e uma classe a mais na coleção seria a <b>15ª</b> —
/// par próprio de containers de Postgres e RabbitMQ, e recalibração obrigatória
/// da referência de duração da suíte (6m37s com 14 classes e zero containers de
/// dev, convenção 22). Não há nada aqui que justifique esse custo.
/// </para>
///
/// <para>
/// O host é construído de verdade, com <c>Configure&lt;EmbeddingOptions&gt;</c>,
/// para que o teste exercite a extensão pelo mesmo caminho do <c>Program.cs</c>
/// — resolução por DI incluída — e não uma função pura por dentro. O que ele
/// <b>não</b> prova é o registro da chamada no <c>Program.cs</c>; isso é
/// conferência manual de escopo.
/// </para>
/// </summary>
public class EmbeddingBatchSizeValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-250)]
    public void NonPositiveBatchSize_FailsTheBoot_AndNamesTheValueFound(int batchSize)
    {
        using var host = BuildHost(batchSize);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateEmbeddingBatchSize);

        // Nomeia o valor ENCONTRADO — é o que permite corrigir sem adivinhar.
        Assert.Contains(batchSize.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("Embedding:BatchSize", exception.Message, StringComparison.Ordinal);
    }

    // O par. Sem ele, a checagem poderia reprovar SEMPRE e este arquivo ficaria
    // verde do mesmo jeito.
    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(10_000)]
    public void PositiveBatchSize_Boots(int batchSize)
    {
        using var host = BuildHost(batchSize);

        host.ValidateEmbeddingBatchSize();
    }

    // E o default, que é o valor que produção usa quando ninguém configura nada:
    // ele tem de passar pela própria checagem.
    [Fact]
    public void TheDefaultBatchSize_Boots()
    {
        Assert.True(new EmbeddingOptions().BatchSize > 0);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<EmbeddingOptions>(_ => { });
        using var host = builder.Build();

        host.ValidateEmbeddingBatchSize();
    }

    /// <summary>
    /// Escopo 4 da change <c>compactacao-historico</c> (D6): a checagem que passa
    /// registra uma linha.
    ///
    /// <para>
    /// <b>Por que isto é guarda e não enfeite.</b> Sucesso indistinguível de "a
    /// checagem não rodou" é a forma de silêncio ambíguo que a linha de início do
    /// detector de tasks não-terminais já existe para evitar. Sem a linha, remover
    /// a chamada do <c>Program.cs</c> continuaria deixando esta classe verde —
    /// é conferência manual de escopo — <b>e</b> não deixaria rastro no boot.
    /// </para>
    /// </summary>
    [Fact]
    public void PositiveBatchSize_LogsTheCheckedValue()
    {
        using var host = BuildHost(250, out var logs);

        host.ValidateEmbeddingBatchSize();

        Assert.Contains(
            logs,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("250", StringComparison.Ordinal)
                && entry.Message.Contains("lote", StringComparison.OrdinalIgnoreCase));
    }

    private static IHost BuildHost(int batchSize)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<EmbeddingOptions>(options => options.BatchSize = batchSize);
        return builder.Build();
    }

    private static IHost BuildHost(int batchSize, out List<(LogLevel Level, string Message)> capturedLogs)
    {
        var logs = new List<(LogLevel Level, string Message)>();
        capturedLogs = logs;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<EmbeddingOptions>(options => options.BatchSize = batchSize);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StartupLogCapture(logs));

        return builder.Build();
    }

    /// <summary>Captura nível e mensagem — idioma de <c>TimeZoneStartupValidationTests</c>.</summary>
    internal sealed class StartupLogCapture(List<(LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
