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
    //
    // ESTE É O PAR DO GUARDA DE LOTEAMENTO, e passa nos dois lados da correção.
    // Ele existe para reprovar a CORREÇÃO ERRADA — lotear sempre, inclusive
    // quando não precisa. Documento que cabe no lote continua sendo uma chamada
    // só, e o dia em que virar N chamadas de um fragmento cada, é aqui que
    // aparece.
    [Fact]
    public async Task DocumentSmallerThanTheBatch_IsGeneratedInExactlyOneCall()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.Equal(1, harness.Embeddings.CallCount);
        Assert.Equal(fragmentCount, harness.Embeddings.LastBatchSize);
    }

    // ---------------------------------------------------------------------
    // Guardas de loteamento da chamada ao gerador de embedding.
    //
    // O DEFEITO MEDIDO (piloto, 20–21/09/2026, gateway do .env.prod, modelo de
    // 4.096 dimensões): o documento inteiro ia numa chamada só, e ~442
    // fragmentos derrubaram o gateway com 502 nas três tentativas, enquanto 267
    // passaram. A propriedade afirmada aqui é escala-livre — "mais fragmentos
    // que o lote produz mais de uma chamada" — e por isso os cenários usam LOTE
    // PEQUENO contra documento pequeno, em vez de semear 442 fragmentos de
    // vetor(4096) num Postgres real (ver MultiFragmentMarkdown).
    //
    // As asserções são sobre NÚMERO e TAMANHO das chamadas. Nunca sobre texto de
    // mensagem de erro — foi o que já mordeu nesta linha de trabalho.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DocumentLargerThanTheBatch_IsGeneratedInMoreThanOneCall()
    {
        const int BatchSize = 2;
        const int Sections = 5;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: BatchSize);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(Sections));

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);

        // Precondição do cenário, afirmada: sem MAIS fragmentos que o lote, o
        // guarda não estaria exercitando loteamento nenhum.
        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.Equal(Sections, fragmentCount);
        Assert.True(fragmentCount > BatchSize);

        // Mais de uma chamada — é o que reprova contra HEAD, onde é sempre uma.
        Assert.True(harness.Embeddings.CallCount > 1);

        // E o tamanho de CADA uma: nenhuma acima do lote, e a soma igual ao
        // número de fragmentos (nenhum perdido, nenhum enviado duas vezes).
        Assert.Equal([2, 2, 1], harness.Embeddings.BatchSizes);
        Assert.All(harness.Embeddings.BatchSizes, size => Assert.True(size <= BatchSize));
        Assert.Equal(fragmentCount, harness.Embeddings.BatchSizes.Sum());
    }

    // A FRONTEIRA, que nenhum dos dois outros pega: o off-by-one do Chunk.
    // Exatamente o tamanho do lote tem de continuar sendo uma chamada só.
    [Fact]
    public async Task DocumentExactlyTheSizeOfTheBatch_IsGeneratedInOneCall()
    {
        const int Sections = 3;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: Sections);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(Sections));

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.Equal(Sections, fragmentCount);
        Assert.Equal([Sections], harness.Embeddings.BatchSizes);
    }

    /// <summary>
    /// <b>O guarda que mais importa, e o menos óbvio: é o ÚNICO modo de o
    /// loteamento corromper o índice em silêncio.</b>
    ///
    /// <para>
    /// Vetor trocado entre fragmentos não falha nada — não há exceção, a
    /// contagem bate, o documento termina <c>Indexed</c>. A busca continua
    /// respondendo, com o conteúdo errado. O sintoma aparece meses depois, como
    /// resposta estranha do agente que ninguém liga a isto. É a mesma família da
    /// divergência de modelo que <c>ValidateEmbeddingIndexConsistency</c>
    /// cobre, e por isso ganha guarda próprio.
    /// </para>
    ///
    /// <para>
    /// <b>Três condições, sem as quais a asserção não discrimina</b>, e as três
    /// estão afirmadas abaixo: textos distintos por fragmento (senão dois
    /// vetores trocados casam), a troca procurada é na FRONTEIRA de lote (é ali
    /// que a concatenação desalinha), e a asserção é por fragmento, nunca sobre
    /// o conjunto — conferir conjuntos passa com dois trocados entre si.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EachFragmentKeepsTheVectorOfItsOwnText_AcrossTheBatchBoundary()
    {
        const int BatchSize = 2;
        const int Sections = 5;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: BatchSize);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(Sections));

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var fragments = await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == documentId)
            .OrderBy(f => f.Ordinal)
            .ToListAsync();

        Assert.Equal(Sections, fragments.Count);
        Assert.True(fragments.Count > BatchSize);

        // Precondição contra VACUIDADE: se dois fragmentos esperassem o mesmo
        // vetor, a asserção abaixo ficaria verde com os dois trocados — que é o
        // defeito que este guarda existe para pegar.
        var expected = fragments
            .Select(f => FakeEmbeddingGenerator.VectorFor(f.Text, KnowledgeIndexingHarness.Dimensions))
            .ToList();
        Assert.Equal(expected.Count, expected.Select(v => string.Join(',', v)).Distinct().Count());

        // Um a um, por ordinal — nunca sobre o conjunto.
        for (var i = 0; i < fragments.Count; i++)
        {
            Assert.Equal(expected[i], fragments[i].Embedding.ToArray());
        }

        // E o par da fronteira, nomeado: último do lote 1 contra primeiro do
        // lote 2. É o que um off-by-one na concatenação troca.
        Assert.Equal(expected[BatchSize - 1], fragments[BatchSize - 1].Embedding.ToArray());
        Assert.Equal(expected[BatchSize], fragments[BatchSize].Embedding.ToArray());
        Assert.NotEqual(fragments[BatchSize - 1].Embedding.ToArray(), fragments[BatchSize].Embedding.ToArray());
    }

    // A conferência de contagem é POR LOTE: um lote que devolve menos vetores do
    // que recebeu entradas falha a indexação, e nada é gravado.
    [Fact]
    public async Task BatchReturningFewerVectorsThanSent_FailsIndexing_AndWritesNothing()
    {
        const int BatchSize = 2;
        const int Sections = 5;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: BatchSize);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(Sections));

        harness.Embeddings.OverrideResultCount = 1;

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);
        Assert.Empty(await dbContext.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());
    }

    // O par "sem item" da convenção 5, com a PRECONDIÇÃO afirmada: o texto tem
    // conteúdo, e é a fragmentação que devolve vazio. Sem essa asserção o
    // cenário ficaria verde por nunca alcançar a chamada — a mesma vacuidade que
    // a convenção 8 nomeia no SELECT DISTINCT sobre tabela vazia.
    //
    // Mora aqui, e não só em KnowledgeIndexingZeroFragmentGuardTests, porque o
    // que ele afirma neste arquivo é sobre o LOTEAMENTO: zero fragmentos não
    // vira "um lote vazio enviado assim mesmo".
    [Fact]
    public async Task DocumentWithNoFragments_DoesNotCallTheGeneratorAtAll()
    {
        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), new EmptyChunker(), batchSize: 2);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(5));

        // Precondição: o documento TEM conteúdo — o vazio vem do fragmentador.
        var document = await dbContext.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId);
        Assert.False(string.IsNullOrWhiteSpace(document.ExtractedText));
        Assert.Empty(new EmptyChunker().Chunk(document.ExtractedText));

        await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(0, harness.Embeddings.CallCount);
        Assert.Empty(harness.Embeddings.BatchSizes);
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

    /// <summary>
    /// Segunda cópia deste duplo — a primeira é privada de
    /// <c>KnowledgeIndexingZeroFragmentGuardTests</c>. Duas cópias de cinco
    /// linhas é exatamente o ponto em que a convenção 2 manda <b>não</b>
    /// extrair ainda; a terceira paga a mudança para <c>Support/</c>.
    /// </summary>
    private sealed class EmptyChunker : Buteco.Workers.Knowledge.Chunking.IKnowledgeChunker
    {
        public IReadOnlyList<Buteco.Workers.Knowledge.Chunking.ChunkedFragment> Chunk(string extractedText) => [];
    }
}
