using Buteco.Workers.EmbeddingMetrics;
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

    // ===================================================================
    // Guardas da coleta de embedding (change metricas-embedding-coleta).
    // Vermelhos contra HEAD + schema, com as tabelas VAZIAS — é a
    // propriedade que falta, não a compilação (design.md, D11).
    // ===================================================================

    // ESCOPO "grão do lote": uma linha POR CHAMADA, e o par do guarda de
    // loteamento que já existe acima.
    [Fact]
    public async Task DocumentLargerThanTheBatch_WritesOneEmbeddingCallRowPerBatch()
    {
        const int BatchSize = 2;
        const int Sections = 5;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: BatchSize);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (baseId, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(Sections));

        var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);

        // PRECONDIÇÃO AFIRMADA, e não suposta pelo arranjo: sem MAIS fragmentos
        // que o lote este cenário não exercitaria loteamento nenhum, e passaria
        // por vacuidade com uma linha só.
        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.Equal(Sections, fragmentCount);
        Assert.True(fragmentCount > BatchSize);

        var calls = await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId);

        Assert.Equal(3, calls.Count);
        Assert.Equal(fragmentCount, calls.Sum(call => call.InputCount));
        Assert.All(calls, call => Assert.True(call.InputCount <= BatchSize));
        Assert.All(calls, call => Assert.Equal(EmbeddingMetricsValues.Purpose.Indexing, call.Purpose));
        Assert.All(calls, call => Assert.Equal(baseId, call.KnowledgeBaseId));
        Assert.All(calls, call => Assert.Equal(KnowledgeIndexingHarness.Dimensions, call.Dimensions));
        Assert.All(calls, call => Assert.False(call.Failed));

        // O par do grão: cada linha é de UMA chamada, e as contagens batem com o
        // que o duplo viu. Sem isto, uma linha por documento com a soma passaria.
        //
        // COMPARADO COMO CONJUNTO (ordenado), e não como sequência: a tabela NÃO
        // tem coluna de ordem, e nada promete que a leitura devolva as linhas na
        // ordem em que os lotes rodaram — medido, devolve [1,2,2] onde os lotes
        // foram [2,2,1]. A propriedade que o desenho promete é "uma linha por
        // chamada, com o tamanho de cada chamada"; afirmar a sequência seria o
        // teste exigindo mais do que o schema garante.
        Assert.Equal(
            harness.Embeddings.BatchSizes.Order(),
            calls.Select(call => call.InputCount).Order());
    }

    // O PAR: documento que cabe no lote grava UMA linha. Existe para reprovar a
    // correção errada — gravar uma linha por fragmento.
    [Fact]
    public async Task DocumentSmallerThanTheBatch_WritesExactlyOneEmbeddingCallRow()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);
        Assert.True(fragmentCount < KnowledgeIndexingHarness.NoBatching);

        var call = Assert.Single(
            await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId));

        Assert.Equal(fragmentCount, call.InputCount);
        Assert.Null(call.TaskId);
        Assert.NotNull(call.KnowledgeIndexingAttemptId);
    }

    // ESCOPO "status HTTP": o 502 que motivou o loteamento, virando consulta.
    // Foi a ausência desta linha que obrigou a reproduzir aquele erro à mão em
    // 20/09/2026.
    //
    // ClientResultException e NÃO HttpRequestException de propósito: é a exceção
    // que o caminho `openai` realmente lança, e ela deriva de Exception
    // (verificado por execução, design.md, D6). Um guarda com
    // HttpRequestException passaria por outro braço do HttpStatusOf e não
    // provaria nada sobre o gateway real.
    [Fact]
    public async Task GatewayFailureWithTypedStatus_WritesFailedRowWithTheHttpStatus()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = UpstreamError;

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var call = Assert.Single(
            await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId));

        Assert.True(call.Failed);
        Assert.Equal(502, call.HttpStatus);
        Assert.Null(call.InputTokens);

        // E a fase, no pai — o status diz O QUE o gateway respondeu, a fase diz
        // ONDE o código estava. Os dois, e não um no lugar do outro (D5).
        var attempt = await EmbeddingMetricsReader.SingleAttemptAsync(fixture.Postgres.GetConnectionString(), documentId);
        Assert.Equal(EmbeddingMetricsValues.FailurePhase.EmbeddingGateway, attempt.FailurePhase);
    }

    // O par negativo: sem status tipado, HttpStatus fica NULO — "não se sabe",
    // nunca um código inventado.
    [Fact]
    public async Task GatewayFailureWithoutTypedStatus_WritesFailedRowWithNullHttpStatus()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.ThrowOnNextCall = () => new InvalidOperationException("sem status tipado");

        await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        var call = Assert.Single(
            await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId));

        Assert.True(call.Failed);
        Assert.Null(call.HttpStatus);
    }

    // ESCOPO "falha no segundo lote": o consumo do primeiro lote ACONTECEU e foi
    // cobrado, mesmo com o documento terminando Failed e nenhum fragmento
    // gravado. Uma soma por documento teria de escolher entre mentir e perder.
    [Fact]
    public async Task FailureOnTheSecondBatch_KeepsTheRowOfTheFirst()
    {
        const int BatchSize = 2;

        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), batchSize: BatchSize);
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(5));

        harness.Embeddings.ThrowOnCall = call => call == 2 ? UpstreamError() : null;

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var calls = await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId);

        Assert.Equal(2, calls.Count);
        Assert.False(calls.First(call => !call.Failed).Failed);
        var failed = calls.Single(call => call.Failed);
        Assert.Equal(502, failed.HttpStatus);

        // E nenhum fragmento gravado — a coleta não alterou a garantia de "tudo
        // ou nada" da indexação.
        Assert.Empty(await dbContext.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());
    }

    // ESCOPO "nulo não é zero" (convenção 13), com o GUARDA NEGATIVO: a asserção
    // afirma a AUSÊNCIA do zero, e não só a presença do nulo. Sem a segunda
    // asserção, uma normalização para zero passaria.
    //
    // Os DOIS estados de "não reportou" — Usage ausente e Usage sem contagem —
    // porque são caminhos diferentes no código que lê.
    [Theory]
    [InlineData(EmbeddingUsageShape.None)]
    [InlineData(EmbeddingUsageShape.WithoutInputCount)]
    public async Task ProviderReportsNoInputCount_WritesNullInputTokens_AndNotZero(
        EmbeddingUsageShape shape)
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.UsageShape = shape;

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var call = Assert.Single(
            await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId));

        Assert.Null(call.InputTokens);
        Assert.NotEqual(0, call.InputTokens);
    }

    // O par: zero REPORTADO é gravado como zero. Sem ele, "nunca grave zero"
    // passaria — e seria a convenção 13 na direção errada.
    [Theory]
    [InlineData(0)]
    [InlineData(1234)]
    public async Task ProviderReportsInputCount_WritesTheReportedValue(long reported)
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        harness.Embeddings.UsageShape = EmbeddingUsageShape.WithInputCount;
        harness.Embeddings.ReportedInputTokens = reported;

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var call = Assert.Single(
            await EmbeddingMetricsReader.CallsForDocumentAsync(fixture.Postgres.GetConnectionString(), documentId));

        Assert.Equal(reported, call.InputTokens);
    }

    // ESCOPO "linha de tentativa": o desfecho feliz, com o par "sem item" antes.
    [Fact]
    public async Task SuccessfulIndexing_WritesAClosedAttemptRow()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (baseId, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        // Par "sem item": antes de indexar, nenhuma tentativa foi afirmada.
        Assert.Empty(await EmbeddingMetricsReader.AttemptsAsync(fixture.Postgres.GetConnectionString(), documentId));

        await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

        var attempt = await EmbeddingMetricsReader.SingleAttemptAsync(fixture.Postgres.GetConnectionString(), documentId);
        var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);

        Assert.Equal(EmbeddingMetricsValues.Outcome.Indexed, attempt.Outcome);
        Assert.Equal(1, attempt.Attempt);
        Assert.Equal(KnowledgeIndexingQueues.MaxAttempts, attempt.MaxAttempts);
        Assert.Equal(baseId, attempt.KnowledgeBaseId);
        Assert.Equal(1, attempt.ContentRevision);
        Assert.Null(attempt.FailurePhase);
        Assert.Equal(fragmentCount, attempt.FragmentCount);
        Assert.True(attempt.EndedAt >= attempt.StartedAt);
    }

    // ESCOPO "M30": três tentativas consultáveis, com a terceira em Failed. É a
    // métrica inteira — sem esta tabela, "falhas de indexação num período" não
    // tem de onde sair, porque knowledge_documents guarda só o estado corrente.
    [Fact]
    public async Task ThreeFailedAttempts_LeaveThreeQueryableAttemptRows()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        for (var attempt = 1; attempt <= KnowledgeIndexingQueues.MaxAttempts; attempt++)
        {
            harness.Embeddings.ThrowOnNextCall = UpstreamError;
            await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1, attempt), default);
        }

        var attempts = await EmbeddingMetricsReader.AttemptsAsync(fixture.Postgres.GetConnectionString(), documentId);

        Assert.Equal(3, attempts.Count);
        Assert.Equal([1, 2, 3], attempts.Select(row => row.Attempt));
        Assert.Equal(
            [
                EmbeddingMetricsValues.Outcome.RetryScheduled,
                EmbeddingMetricsValues.Outcome.RetryScheduled,
                EmbeddingMetricsValues.Outcome.Failed,
            ],
            attempts.Select(row => row.Outcome));
        Assert.All(attempts, row => Assert.Equal(EmbeddingMetricsValues.FailurePhase.EmbeddingGateway, row.FailurePhase));
        Assert.All(attempts, row => Assert.Null(row.FragmentCount));
    }

    // ESCOPO "fase da falha": vocabulário fechado, determinado por ONDE o código
    // estava — nunca pelo texto da exceção, que é de quem a lança e muda sem
    // aviso (D5).
    [Theory]
    [InlineData(EmbeddingFailureKind.ProviderResolution, EmbeddingMetricsValues.FailurePhase.ProviderResolution)]
    [InlineData(EmbeddingFailureKind.Gateway, EmbeddingMetricsValues.FailurePhase.EmbeddingGateway)]
    [InlineData(EmbeddingFailureKind.VectorCount, EmbeddingMetricsValues.FailurePhase.VectorCountMismatch)]
    [InlineData(EmbeddingFailureKind.Dimension, EmbeddingMetricsValues.FailurePhase.DimensionMismatch)]
    public async Task FailurePhase_IsTheStepWhereItStopped(EmbeddingFailureKind kind, string expectedPhase)
    {
        var harness = kind == EmbeddingFailureKind.ProviderResolution
            ? KnowledgeIndexingHarness.BuildWithThrowingResolver(fixture.Postgres.GetConnectionString())
            : KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());

        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());

        // DOCUMENTO MULTI-FRAGMENTO, e não o `Markdown` curto — a diferença é o
        // caso `VectorCount`. Aquele texto produz UM fragmento, e truncar a
        // resposta para um vetor é então um NO-OP: a contagem confere, a
        // indexação termina `Indexed`, e o cenário ficaria verde sem nunca
        // exercitar a divergência. Foi o que aconteceu na primeira execução
        // deste guarda — reprovou pelo motivo errado, e o enunciado mudou antes
        // da correção.
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(
            dbContext, KnowledgeIndexingHarness.MultiFragmentMarkdown(3));

        switch (kind)
        {
            case EmbeddingFailureKind.Gateway:
                harness.Embeddings.ThrowOnNextCall = UpstreamError;
                break;
            case EmbeddingFailureKind.VectorCount:
                harness.Embeddings.OverrideResultCount = 1;
                break;
            case EmbeddingFailureKind.Dimension:
                harness.Embeddings.OverrideDimensions = KnowledgeIndexingHarness.Dimensions - 1;
                break;
        }

        // Precondição afirmada para o caso `VectorCount`: só há divergência de
        // contagem se houver MAIS de um fragmento para truncar.
        if (kind == EmbeddingFailureKind.VectorCount)
        {
            Assert.True(new Buteco.Workers.Knowledge.Chunking.KnowledgeChunker()
                .Chunk(KnowledgeIndexingHarness.MultiFragmentMarkdown(3)).Count > 1);
        }

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var attempt = await EmbeddingMetricsReader.SingleAttemptAsync(fixture.Postgres.GetConnectionString(), documentId);
        Assert.Equal(expectedPhase, attempt.FailurePhase);
    }

    // ESCOPO "degradação graciosa": sem a tabela de métrica, a indexação termina
    // exatamente como terminaria com ela. A escrita acontece DEPOIS do estado do
    // documento, então não há ordem de execução em que ela altere o que a
    // indexação terminou sendo (D7).
    [Fact]
    public async Task MetricsTableMissing_DoesNotChangeTheIndexingOutcome()
    {
        var harness = KnowledgeIndexingHarness.Build(fixture.Postgres.GetConnectionString());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, Markdown);

        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE knowledge_indexing_attempts RENAME TO knowledge_indexing_attempts_hidden;");
        try
        {
            var outcome = await harness.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);

            Assert.Equal(KnowledgeIndexingOutcome.Indexed, outcome);

            var after = await ReadAsync(dbContext, documentId);
            var fragmentCount = await dbContext.KnowledgeFragments.CountAsync(f => f.KnowledgeDocumentId == documentId);

            Assert.Equal(KnowledgeIndexingStatus.Indexed, after.IndexingStatus);
            Assert.Equal(fragmentCount, after.FragmentCount);
            Assert.True(fragmentCount > 0);
            Assert.Null(after.FailureReason);
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "ALTER TABLE knowledge_indexing_attempts_hidden RENAME TO knowledge_indexing_attempts;");
        }
    }

    /// <summary>
    /// A exceção do caminho <c>openai</c> para um <c>502</c> do gateway.
    /// <c>ClientResultException</c> deriva de <see cref="Exception"/>, e NÃO de
    /// <see cref="HttpRequestException"/> — verificado por execução (design.md,
    /// D6). É o tipo que o <c>HttpStatusOf</c> precisa alcançar pelo braço
    /// certo, e usar <c>HttpRequestException</c> aqui não provaria nada sobre o
    /// gateway real.
    /// </summary>
    private static Exception UpstreamError() =>
        new System.ClientModel.ClientResultException(new StubPipelineResponse(502));

    public enum EmbeddingFailureKind
    {
        ProviderResolution,
        Gateway,
        VectorCount,
        Dimension,
    }

    /// <summary>
    /// Infraestrutura de duplo: o mínimo de <c>PipelineResponse</c> para
    /// construir um <c>ClientResultException</c> com status. Os membros que os
    /// guardas não leem estouram de propósito — se algum dia forem lidos, o
    /// teste diz isso em vez de devolver um valor inventado.
    /// </summary>
    private sealed class StubPipelineResponse(int status) : System.ClientModel.Primitives.PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => "upstream_error";

        protected override System.ClientModel.Primitives.PipelineResponseHeaders HeadersCore =>
            throw new NotSupportedException();

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            new(Content);

        public override void Dispose()
        {
        }
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
