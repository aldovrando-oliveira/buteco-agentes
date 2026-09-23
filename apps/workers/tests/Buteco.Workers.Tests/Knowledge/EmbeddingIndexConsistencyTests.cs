using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Checagem de integridade do índice no boot — quinto caso da convenção 8,
/// bidirecional.
///
/// <para>
/// <b>A quinta forma da convenção 15 é o que decide se este guarda vale, e é
/// por ela que este arquivo está escrito assim.</b> <c>SELECT DISTINCT</c> sobre
/// tabela vazia devolve conjunto vazio, e conjunto vazio sobe — então um guarda
/// exercitado só contra <b>estado limpo</b> fica verde com e sem a
/// implementação, porque nunca chega à comparação. Todo cenário de divergência
/// aqui roda com o índice <b>povoado</b>, e há um teste explícito afirmando que
/// o caso vazio não é o que dá a confiança.
/// </para>
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class EmbeddingIndexConsistencyTests(WorkerInfrastructureFixture fixture)
    : IClassFixture<WorkerInfrastructureFixture>
{
    private const string Markdown = "# Política\n\n## Faixas\n\nAté trinta dias não há desconto.\n";

    // ------------------------------------------------------------- sobe ----

    [Fact]
    public async Task EmptyIndex_Boots()
    {
        await ResetIndexAsync();

        var host = BuildHost(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        host.ValidateEmbeddingIndexConsistency();
    }

    [Fact]
    public async Task IndexConsistentWithConfiguration_Boots()
    {
        await ResetIndexAsync();

        await SeedIndexedDocumentAsync(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        var host = BuildHost(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        host.ValidateEmbeddingIndexConsistency();
    }

    // ------------------------------------------------------- não sobe ----

    [Fact]
    public async Task DivergentModel_FailsTheBoot_AndNamesBothSides()
    {
        await ResetIndexAsync();

        await SeedIndexedDocumentAsync("modelo-que-estava-no-indice", KnowledgeIndexingHarness.Dimensions);

        var host = BuildHost("modelo-declarado-agora", KnowledgeIndexingHarness.Dimensions);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateEmbeddingIndexConsistency);

        // Nomeia os dois lados — idioma de ValidateKnowledgeExtractorRegistrations.
        Assert.Contains("modelo-declarado-agora", exception.Message, StringComparison.Ordinal);
        Assert.Contains("modelo-que-estava-no-indice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DivergentDimensions_FailsTheBoot()
    {
        await ResetIndexAsync();

        await SeedIndexedDocumentAsync(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        // Mesma coluna vector(4096), dimensão DECLARADA diferente: é o caso em
        // que a configuração mudou e o índice não.
        var host = BuildHost(KnowledgeIndexingHarness.Model, 1536);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateEmbeddingIndexConsistency);
        Assert.Contains("1536", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Duas combinações no índice reprovam <b>ainda que uma delas seja a
    /// declarada</b> — é corrupção por troca anterior não detectada, e metade dos
    /// vetores é incomparável com a outra metade.
    /// </summary>
    [Fact]
    public async Task TwoModelsInTheIndex_FailTheBoot_EvenWhenOneMatches()
    {
        await ResetIndexAsync();

        await SeedIndexedDocumentAsync(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);
        await SeedIndexedDocumentAsync("modelo-de-antes", KnowledgeIndexingHarness.Dimensions);

        var host = BuildHost(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        var exception = Assert.Throws<InvalidOperationException>(host.ValidateEmbeddingIndexConsistency);
        Assert.Contains("corrompido", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------ a quinta forma, explícita ----

    /// <summary>
    /// <b>O caso vazio não dá confiança nenhuma, e este teste existe para dizer
    /// isso por escrito.</b>
    ///
    /// <para>
    /// Uma implementação que não compare nada — que só retorne — passa em
    /// <c>EmptyIndex_Boots</c> e em <c>IndexConsistentWithConfiguration_Boots</c>
    /// exatamente como a correta. O que separa as duas são os cenários de
    /// divergência acima, e eles só existem porque o índice é <b>semeado</b>
    /// antes. Este teste afirma a precondição que torna os outros capazes de
    /// reprovar: depois de semear, a tabela tem linha.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DivergenceScenariosRunAgainstAPopulatedIndex_NotACleanOne()
    {
        await ResetIndexAsync();

        await using var dbContext = NewDbContext();
        Assert.Equal(0, await dbContext.KnowledgeFragments.CountAsync());

        await SeedIndexedDocumentAsync("modelo-qualquer", KnowledgeIndexingHarness.Dimensions);

        await using var check = NewDbContext();
        Assert.True(
            await check.KnowledgeFragments.CountAsync() > 0,
            "sem linha semeada o SELECT DISTINCT devolve vazio e a checagem passa por vacuidade — "
          + "os cenários de divergência não estariam exercitando nada");
    }

    // ------------------------------------------------------------ apoio ----

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private static Task ClearFragmentsAsync(AppDbContext dbContext) =>
        dbContext.Database.ExecuteSqlRawAsync("DELETE FROM knowledge_fragments;");

    /// <summary>
    /// Limpa o índice. <b>Obrigatório no começo de cada teste desta classe:</b>
    /// o fixture é por classe, os testes compartilham o mesmo Postgres, e
    /// fragmentos de um teste anterior fariam o seguinte cair no caminho de
    /// "mais de uma combinação" em vez do que ele quer exercitar. Foi
    /// exatamente o que aconteceu na primeira execução — dois testes reprovaram
    /// por contaminação entre si, não por defeito do guarda.
    /// </summary>
    private async Task ResetIndexAsync()
    {
        await using var dbContext = NewDbContext();
        await ClearFragmentsAsync(dbContext);
    }

    private async Task SeedIndexedDocumentAsync(string model, int dimensions)
    {
        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), dimensions: dimensions, model: model);

        await using var dbContext = NewDbContext();
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);
    }

    /// <summary>
    /// Escopo 4 da change <c>compactacao-historico</c> (D6): os DOIS caminhos de
    /// sucesso desta checagem registram linha, e dizem coisas diferentes —
    /// "índice vazio, primeiro deploy" não é "conferido contra o índice".
    ///
    /// <para>
    /// <b>Por que agora.</b> A única manifestação desta checagem no log era a
    /// consulta que o EF Core imprimia, e o mesmo escopo a silencia em produção
    /// (`Microsoft.EntityFrameworkCore.Database.Command` em `Warning`). Sem
    /// linha própria, ela passaria a rodar invisível — a change teria tornado
    /// uma checagem de boot muda.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EmptyIndex_LogsThatTheIndexIsEmpty()
    {
        await ResetIndexAsync();

        using var host = BuildHost(
            KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions, out var logs);

        host.ValidateEmbeddingIndexConsistency();

        Assert.Contains(
            logs,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("vazio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IndexConsistentWithConfiguration_LogsWhatWasChecked()
    {
        await ResetIndexAsync();
        await SeedIndexedDocumentAsync(KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions);

        using var host = BuildHost(
            KnowledgeIndexingHarness.Model, KnowledgeIndexingHarness.Dimensions, out var logs);

        host.ValidateEmbeddingIndexConsistency();

        Assert.Contains(
            logs,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains(KnowledgeIndexingHarness.Model, StringComparison.Ordinal)
                && entry.Message.Contains(
                    KnowledgeIndexingHarness.Dimensions.ToString(), StringComparison.Ordinal));
    }

    private IHost BuildHost(
        string model, int dimensions, out List<(LogLevel Level, string Message)> capturedLogs)
    {
        var logs = new List<(LogLevel Level, string Message)>();
        capturedLogs = logs;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()));
        builder.Services.Configure<EmbeddingOptions>(options =>
        {
            options.Provider = KnowledgeIndexingHarness.Provider;
            options.Model = model;
            options.Dimensions = dimensions;
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new EmbeddingBatchSizeValidationTests.StartupLogCapture(logs));

        return builder.Build();
    }

    private IHost BuildHost(string model, int dimensions)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()));
        builder.Services.Configure<EmbeddingOptions>(options =>
        {
            options.Provider = KnowledgeIndexingHarness.Provider;
            options.Model = model;
            options.Dimensions = dimensions;
        });

        return builder.Build();
    }
}
