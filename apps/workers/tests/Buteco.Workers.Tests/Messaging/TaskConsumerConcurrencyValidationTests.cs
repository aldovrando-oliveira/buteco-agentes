using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupLogCapture = Buteco.Workers.Tests.Knowledge.EmbeddingBatchSizeValidationTests.StartupLogCapture;

namespace Buteco.Workers.Tests.Messaging;

/// <summary>
/// Guarda 5 da change workers-consumo-concorrente (D7): checagem de boot de
/// <c>TaskConsumer:MaxConcurrentExecutions</c>.
///
/// <para>
/// <b>Sem <c>WorkerInfrastructureFixture</c> e fora da coleção de hosts, de
/// propósito</b>, pelo mesmo motivo de <c>EmbeddingBatchSizeValidationTests</c>: a
/// checagem não toca banco nem fila, e uma classe a mais na coleção custaria um par
/// de containers e a recalibração da referência de duração da suíte.
/// </para>
///
/// <para>
/// O host é construído de verdade, com <c>Configure&lt;TaskConsumerOptions&gt;</c>,
/// para exercitar a extensão pelo caminho do <c>Program.cs</c>. O que ele <b>não</b>
/// prova é o registro da chamada no <c>Program.cs</c>.
/// </para>
/// </summary>
public class TaskConsumerConcurrencyValidationTests
{
    // As duas bordas de fora e o negativo. 0 é o caso que motiva a checagem:
    // prefetch 0 no RabbitMQ é "sem limite".
    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    [InlineData(-1)]
    public void OutOfRange_FailsTheBoot_AndNamesTheValueAndTheRange(int value)
    {
        using var host = BuildHost(value, out _);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateTaskConsumerConcurrency);

        Assert.Contains($"configurado como {value},", exception.Message, StringComparison.Ordinal);
        Assert.Contains("TaskConsumer:MaxConcurrentExecutions", exception.Message, StringComparison.Ordinal);
        Assert.Contains("[1, 32]", exception.Message, StringComparison.Ordinal);
        // O motivo do teto (spec: "N acima do teto reprova o boot").
        Assert.Contains("pool de conexões do Postgres", exception.Message, StringComparison.Ordinal);
    }

    // O par: as bordas de dentro passam, com a linha que nomeia o valor. Sem ele a
    // checagem poderia reprovar sempre e a teoria de cima ficaria verde.
    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void InRange_Boots_AndLogsTheValueInForce(int value)
    {
        using var host = BuildHost(value, out var logs);

        host.ValidateTaskConsumerConcurrency();

        Assert.Contains(logs, entry => entry.Level == LogLevel.Information && entry.Message.EndsWith($": {value}.", StringComparison.Ordinal));
    }

    // O default é o que produção usa sem configuração: tem de ser 4 (spec "Valor
    // padrão de N declarado e não medido") e passar pela própria checagem.
    [Fact]
    public void TheDefault_IsFour_AndBoots()
    {
        Assert.Equal(4, new TaskConsumerOptions().MaxConcurrentExecutions);

        var logs = new List<(LogLevel Level, string Message)>();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<TaskConsumerOptions>(_ => { });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StartupLogCapture(logs));
        using var host = builder.Build();

        host.ValidateTaskConsumerConcurrency();

        Assert.Contains(logs, entry => entry.Level == LogLevel.Information && entry.Message.EndsWith(": 4.", StringComparison.Ordinal));
    }

    private static IHost BuildHost(int value, out List<(LogLevel Level, string Message)> capturedLogs)
    {
        var logs = new List<(LogLevel Level, string Message)>();
        capturedLogs = logs;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<TaskConsumerOptions>(options => options.MaxConcurrentExecutions = value);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StartupLogCapture(logs));

        return builder.Build();
    }
}
