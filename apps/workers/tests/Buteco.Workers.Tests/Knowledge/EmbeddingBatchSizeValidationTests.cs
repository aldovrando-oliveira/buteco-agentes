using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    private static IHost BuildHost(int batchSize)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<EmbeddingOptions>(options => options.BatchSize = batchSize);
        return builder.Build();
    }
}
