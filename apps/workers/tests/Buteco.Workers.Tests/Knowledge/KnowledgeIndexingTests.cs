using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Indexação fim a fim contra Postgres real, com o provedor de embedding
/// substituído. Cobre a máquina de estados, as três garantias de D9 da etapa 1 e
/// a política de tentativas.
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class KnowledgeIndexingTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string Markdown = """
        # Política de cobrança

        ## Faixas de atraso

        Até trinta dias não há desconto sobre o principal, apenas a retirada dos
        encargos de mora.

        ## Parcelamento

        Nenhuma parcela pode ficar abaixo de cinquenta reais.
        """;

    [Fact]
    public async Task Document_TransitionsFromPendingToIndexed_WithFragmentsAndDate()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        // Par "sem item": antes do consumidor, nada foi afirmado.
        var before = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Pending, before.IndexingStatus);
        Assert.Null(before.IndexedAt);
        Assert.Equal(0, before.IndexingAttempts);
        Assert.Null(before.LastAttemptAt);

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);

        var after = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Indexed, after.IndexingStatus);
        Assert.NotNull(after.IndexedAt);
        Assert.Null(after.FailureReason);

        var fragments = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId).ToListAsync();

        Assert.NotEmpty(fragments);
        Assert.Equal(fragments.Count, after.FragmentCount);
        Assert.All(fragments, f => Assert.Equal(KnowledgeIndexingHarness.Dimensions, f.EmbeddingDimensions));
        Assert.All(fragments, f => Assert.Equal(KnowledgeIndexingHarness.Model, f.EmbeddingModel));
    }

    // O embedding é gerado em LOTE — a interface é batch-nativa, e isso casa com
    // indexação. Uma chamada por fragmento seria N vezes o custo e a latência.
    [Fact]
    public async Task Embeddings_AreGeneratedInASingleBatch()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.Equal(1, harness.Embeddings.CallCount);
        Assert.Equal(fragmentCount, harness.Embeddings.LastBatchSize);
    }

    // Garantia 1 de D9 da etapa 1: substituição integral, nunca diff.
    [Fact]
    public async Task Reindexing_ReplacesTheWholeFragmentSet()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        var firstIds = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId).Select(f => f.Id).ToListAsync();

        // Conteúdo novo, revisão nova — como o PUT faria.
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE knowledge_documents
               SET "ExtractedText" = {Markdown + "\n\n## Protesto\n\nA partir de noventa dias.\n"},
                   "ContentRevision" = 2
             WHERE "Id" = {documentId};
            """);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 2), default);

        var secondIds = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId).Select(f => f.Id).ToListAsync();

        Assert.NotEmpty(secondIds);
        Assert.Empty(secondIds.Intersect(firstIds));
    }

    // Garantia 3 de D9: falha na REINDEXAÇÃO preserva indexedAt e os fragmentos
    // anteriores — o documento continua respondendo com o conteúdo anterior.
    [Fact]
    public async Task FailureOnReindexing_PreservesPreviousFragmentsAndIndexedAt()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        var indexed = await ReadAsync(dbContext, documentId);
        var previousFragmentIds = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId).Select(f => f.Id).ToListAsync();

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE knowledge_documents SET "ContentRevision" = 2 WHERE "Id" = {documentId};
            """);

        harness.Embeddings.ThrowOnNextCall = () => new HttpRequestException(
            "limite", null, System.Net.HttpStatusCode.TooManyRequests);

        // Última tentativa: é a saída que grava Failed.
        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 2, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var failed = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Failed, failed.IndexingStatus);
        Assert.NotNull(failed.FailureReason);
        Assert.Equal(indexed.IndexedAt, failed.IndexedAt);

        var stillThere = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId).Select(f => f.Id).ToListAsync();
        Assert.Equal(previousFragmentIds.Order(), stillThere.Order());
    }

    // O par do caso acima: falha na PRIMEIRA indexação não inventa data. É a
    // distinção que a regra de exibição da contagem depende (D8 da etapa 1).
    [Fact]
    public async Task FailureOnFirstIndexing_LeavesIndexedAtNull()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = () => new HttpRequestException("indisponível");

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var failed = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Failed, failed.IndexingStatus);
        Assert.NotNull(failed.FailureReason);
        Assert.Null(failed.IndexedAt);
        Assert.Empty(await dbContext.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());
    }

    // AS DUAS SAÍDAS DO CATCH, que é onde o molde de TaskJobConsumer não ajuda.
    // Falha com tentativa disponível reagenda e NÃO grava Failed; o documento
    // continua em Indexing, e a tentativa JÁ foi contada.
    [Fact]
    public async Task TransientFailureWithAttemptsLeft_SchedulesRetry_WithoutMarkingFailed()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = () => new HttpRequestException("instável");

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1, 1), default);

        Assert.Equal(KnowledgeIndexingOutcome.RetryScheduled, outcome);

        var afterRetry = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Indexing, afterRetry.IndexingStatus);
        Assert.Null(afterRetry.FailureReason);
        Assert.Equal(1, afterRetry.IndexingAttempts);
        Assert.NotNull(afterRetry.LastAttemptAt);
    }

    // Sucesso depois de falha transitória: as tentativas ficam contadas, e o
    // motivo de falha é limpo.
    [Fact]
    public async Task SuccessAfterTransientFailure_ClearsTheReason_AndKeepsTheAttemptCount()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = () => new HttpRequestException("instável");
        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1, 1), default);

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1, 2), default);

        Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);
        var final = await ReadAsync(dbContext, documentId);
        Assert.Equal(KnowledgeIndexingStatus.Indexed, final.IndexingStatus);
        Assert.Null(final.FailureReason);
        Assert.Equal(2, final.IndexingAttempts);
    }

    // O texto de falha é para o operador: sem nome de tipo, sem pilha. Asserção
    // NEGATIVA, que é a que pega vazamento de detalhe interno.
    [Fact]
    public async Task FailureReason_IsOperatorReadable_WithoutExceptionDetail()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = () => new HttpRequestException(
            "System.Net.Http.HttpRequestException: connection reset at Foo.Bar()",
            null, System.Net.HttpStatusCode.TooManyRequests);

        await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        var reason = (await ReadAsync(dbContext, documentId)).FailureReason;

        Assert.NotNull(reason);
        Assert.Contains("limite de uso", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net", reason, StringComparison.Ordinal);
    }

    // O provedor pode ACEITAR o pedido de dimensão e ignorá-lo — foi medido que
    // aceita. Nada é gravado com dimensão divergente.
    [Fact]
    public async Task DimensionMismatch_FailsIndexing_AndWritesNoFragment()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.OverrideDimensions = 1536;

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);
        Assert.Empty(await dbContext.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());

        var reason = (await ReadAsync(dbContext, documentId)).FailureReason;
        Assert.Contains("1536", reason!, StringComparison.Ordinal);
        Assert.Contains("4096", reason!, StringComparison.Ordinal);
    }

    private static async Task<KnowledgeDocument> ReadAsync(Buteco.Workers.Infrastructure.AppDbContext dbContext, Guid id)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == id);
    }
}
