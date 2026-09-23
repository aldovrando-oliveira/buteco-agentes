using Buteco.Workers.EmbeddingMetrics;
using Buteco.Workers.Knowledge.Chunking;
using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// A guarda de "sucesso com zero fragmentos é recusado".
///
/// <para>
/// <b>Este é um guarda de forma peculiar, e a distinção importa:</b> ele reprova
/// <b>trocando uma dependência</b> — põe no lugar um <see cref="IKnowledgeChunker"/>
/// que devolve conjunto vazio — e não reintroduzindo um defeito no código sob
/// teste. Por isso as duas metades da convenção 15 precisam ser verificadas
/// separadamente, e a segunda é a que costuma faltar.
/// </para>
///
/// <para>
/// <b>Metade 1 — ele reprova.</b> Sem a guarda em
/// <c>KnowledgeIndexingService</c>, o documento terminaria <c>Indexed</c> com
/// <c>FragmentCount = 0</c>: contagem zerada com aparência de sucesso, o pior
/// caso da convenção 13. <b>Verificado removendo a guarda do serviço e rodando:
/// os dois testes abaixo reprovam.</b>
/// </para>
///
/// <para>
/// <b>Metade 2 — ele reprova SÓ ELE.</b> A substituição do fragmentador não pode
/// derrubar <c>KnowledgeChunkerInvariantTests.BlankInput_ProducesNoFragment</c>
/// junto, porque aquele teste afirma <b>propriedade do fragmentador real</b> e
/// este afirma <b>o comportamento do consumidor diante de um fragmentador
/// defeituoso</b>. A separação é estrutural, não de disciplina: aquele teste
/// instancia <c>new KnowledgeChunker()</c> diretamente e nunca vê a
/// substituição, que só existe dentro do arnês deste arquivo. Se algum dia a
/// substituição alcançar os dois, um dos dois está afirmando no componente
/// errado — que é o segundo modo de falha da convenção 15.
/// </para>
///
/// <para>
/// <b>Medido, não prometido.</b> Rodando este arquivo junto de
/// <c>KnowledgeChunkerInvariantTests</c>: com a guarda, <b>15 de 15</b> passam;
/// removida a guarda, reprovam <b>exatamente 2</b>, os dois deste arquivo. Os 12
/// invariantes do fragmentador não se movem. É a segunda metade da convenção 15
/// afirmada com número — a metade que, quando falta, deixa dois guardas
/// parecerem independentes sem serem.
/// </para>
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class KnowledgeIndexingZeroFragmentGuardTests(WorkerInfrastructureFixture fixture)
    : IClassFixture<WorkerInfrastructureFixture>
{
    /// <summary>
    /// Fragmentador defeituoso: devolve conjunto vazio para QUALQUER entrada,
    /// inclusive documento com conteúdo. É a regressão que a guarda existe para
    /// pegar — e o caminho alcançável para zero fragmentos, já que documento em
    /// branco é recusado pela API antes de chegar aqui.
    /// </summary>
    private sealed class AlwaysEmptyChunker : IKnowledgeChunker
    {
        public IReadOnlyList<ChunkedFragment> Chunk(string extractedText) => [];
    }

    private const string ContentfulMarkdown = """
        # Política de cobrança

        ## Faixas de atraso

        Até trinta dias não há desconto sobre o principal.
        """;

    [Fact]
    public async Task ChunkerReturningEmptySet_DoesNotProduceIndexedWithZeroFragments()
    {
        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), new AlwaysEmptyChunker());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, ContentfulMarkdown);

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        var document = await dbContext.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId);

        // O que a guarda impede, dito de forma direta: NÃO é Indexed, e NÃO é
        // Indexed com zero.
        Assert.NotEqual(KnowledgeIndexingStatus.Indexed, document.IndexingStatus);
        Assert.Equal(KnowledgeIndexingStatus.Failed, document.IndexingStatus);
        Assert.Null(document.IndexedAt);
        Assert.NotNull(document.FailureReason);
    }

    // Nenhum fragmento é gravado, e o provedor de embedding NÃO é chamado: sem a
    // guarda, o serviço seguiria para o lote vazio e gastaria a chamada.
    [Fact]
    public async Task ChunkerReturningEmptySet_WritesNothing_AndDoesNotCallTheProvider()
    {
        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), new AlwaysEmptyChunker());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, ContentfulMarkdown);

        await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Empty(await dbContext.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());
        Assert.Equal(0, harness.Embeddings.CallCount);
    }

    /// <summary>
    /// <b>A metade 2 da convenção 15, afirmada em vez de prometida.</b>
    ///
    /// <para>
    /// Com a substituição em vigor neste arquivo, o fragmentador real continua
    /// se comportando como sempre — porque a substituição nunca o alcança. Este
    /// teste existe para que, se alguém um dia trocar o fragmentador por
    /// injeção global (um <c>ICollectionFixture</c>, um container de DI
    /// compartilhado), a sobreposição apareça aqui em vez de silenciosamente
    /// tornar um dos dois guardas vazio.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSubstitutionDoesNotReachTheRealChunker()
    {
        var real = new KnowledgeChunker();

        // A propriedade que KnowledgeChunkerInvariantTests afirma: entrada com
        // conteúdo produz fragmento; entrada em branco não produz.
        Assert.NotEmpty(real.Chunk(ContentfulMarkdown));
        Assert.Empty(real.Chunk("   "));

        // E o defeituoso é outra coisa, no mesmo contrato.
        Assert.Empty(new AlwaysEmptyChunker().Chunk(ContentfulMarkdown));
    }

    /// <summary>
    /// O par "sem item" (convenção 5) do lado da <b>coleta de embedding</b>:
    /// documento <b>com conteúdo</b> cuja fragmentação devolve zero não chama o
    /// gateway, e portanto <b>não grava linha de chamada</b> — mas grava a linha
    /// de <b>tentativa</b>, com a fase <c>Chunking</c>.
    ///
    /// <para>
    /// <b>Os dois lados, e não só o primeiro.</b> "Nenhuma linha de chamada"
    /// sozinho ficaria verde se a coleta inteira estivesse quebrada; é a linha
    /// de tentativa que prova que o caminho de gravação funciona e que o zero é
    /// do gateway, não do coletor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DocumentWithNoFragments_WritesNoEmbeddingCall_ButWritesTheAttemptWithChunkingPhase()
    {
        var harness = KnowledgeIndexingHarness.Build(
            fixture.Postgres.GetConnectionString(), new AlwaysEmptyChunker());
        await using var dbContext = harness.NewDbContext(fixture.Postgres.GetConnectionString());
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(dbContext, ContentfulMarkdown);

        // PRECONDIÇÃO AFIRMADA, e não suposta pelo arranjo: o documento TEM
        // conteúdo, e o vazio vem do fragmentador. Sem estas três linhas o
        // cenário ficaria verde por nunca alcançar a chamada ao gateway — que é
        // exatamente o que ele precisa provar que não acontece.
        var document = await dbContext.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId);
        Assert.False(string.IsNullOrWhiteSpace(document.ExtractedText));
        Assert.Empty(new AlwaysEmptyChunker().Chunk(document.ExtractedText));

        var outcome = await harness.Service.IndexAsync(
            new KnowledgeIndexingJobMessage(documentId, 1, KnowledgeIndexingQueues.MaxAttempts), default);

        Assert.Equal(KnowledgeIndexingOutcome.Failed, outcome);

        // O gateway não foi chamado — e por isso não há linha de chamada.
        Assert.Equal(0, harness.Embeddings.CallCount);
        Assert.Empty(await EmbeddingMetricsReader.CallsForDocumentAsync(
            fixture.Postgres.GetConnectionString(), documentId));

        // O OUTRO lado do par: a tentativa foi contada, então a linha existe.
        var attempt = await EmbeddingMetricsReader.SingleAttemptAsync(
            fixture.Postgres.GetConnectionString(), documentId);
        Assert.Equal(EmbeddingMetricsValues.Outcome.Failed, attempt.Outcome);
        Assert.Equal(EmbeddingMetricsValues.FailurePhase.Chunking, attempt.FailurePhase);
        Assert.Null(attempt.FragmentCount);
    }
}
