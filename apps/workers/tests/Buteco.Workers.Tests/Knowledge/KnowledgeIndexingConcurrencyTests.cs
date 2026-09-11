using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Concorrência real, no molde de <c>AgentDelegationConcurrencyTests</c>: cada
/// instância monta o próprio <c>IServiceScopeFactory</c> do zero, e nada é
/// compartilhado além do Postgres do fixture.
///
/// <para>
/// <b>Não é caminho feliz.</b> O que se afirma aqui é o que acontece quando o
/// operador mexe no documento <b>durante</b> a indexação — atualizando ou
/// excluindo —, que é o caso silencioso: sem o descarte por revisão, fragmentos
/// de uma revisão morta ficariam gravados e a busca responderia com conteúdo que
/// o operador já trocou, sem erro nenhum.
/// </para>
///
/// <para>
/// <b>Os dois testes foram vistos reprovar, e contra defeitos DIFERENTES — o que
/// confirma empiricamente a correção que a verificação V4 fez na D7 da etapa
/// 1.</b> Aquela decisão dizia que <c>ContentRevision</c> "cobre o delete de
/// graça"; medindo, são dois mecanismos independentes:
/// </para>
///
/// <list type="table">
/// <item>
///   <term>removida a condição <c>ContentRevision == ...</c> do commit</term>
///   <description>reprova <b>só</b> o teste de atualização — o de exclusão passa,
///   porque a linha já não existe e a atualização afeta zero linhas de qualquer
///   jeito</description>
/// </item>
/// <item>
///   <term>removida a checagem de <c>linhas afetadas == 0</c></term>
///   <description>reprovam <b>os dois</b> — é ela que transforma "zero linhas" em
///   descarte, e sem ela o caso de exclusão segue para o INSERT e estoura na
///   chave estrangeira</description>
/// </item>
/// </list>
///
/// <para>
/// Ou seja: <c>ContentRevision</c> cobre a <b>atualização</b>; o <b>delete</b> é
/// coberto pela ausência da linha mais a checagem de zero linhas, e quem impede
/// fragmento órfão é a chave estrangeira em <c>Cascade</c>. Três coisas, não uma.
/// </para>
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class KnowledgeIndexingConcurrencyTests(WorkerInfrastructureFixture fixture)
    : IClassFixture<WorkerInfrastructureFixture>
{
    private const string Original = """
        # Política

        ## Faixas

        Até trinta dias não há desconto sobre o principal.
        """;

    private const string Updated = """
        # Política

        ## Faixas

        Até sessenta dias não há desconto sobre o principal, e a regra mudou.
        """;

    /// <summary>
    /// O conteúdo muda enquanto a indexação da revisão anterior está em
    /// andamento. O resultado da revisão velha é <b>descartado inteiro</b> —
    /// nenhum fragmento dela é gravado — e o documento não fica marcado como
    /// indexado por um trabalho que já nasceu obsoleto.
    /// </summary>
    [Fact]
    public async Task DocumentUpdatedDuringIndexing_DiscardsTheStaleResultEntirely()
    {
        var connectionString = fixture.Postgres.GetConnectionString();
        var indexer = KnowledgeIndexingHarness.Build(connectionString);
        await using var seeding = indexer.NewDbContext(connectionString);
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(seeding, Original);

        // A janela de corrida é criada de propósito: o gerador de embedding
        // bloqueia até o teste atualizar o documento, que é exatamente o
        // intervalo em que o trabalho lento acontece na vida real.
        var chegouNoProvedor = new TaskCompletionSource();
        var podeSeguir = new TaskCompletionSource();
        indexer.Embeddings.BeforeGenerate = async () =>
        {
            chegouNoProvedor.TrySetResult();
            await podeSeguir.Task;
        };

        var indexing = indexer.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        await chegouNoProvedor.Task;

        // Outra "instância": contexto próprio, como o apps/api faria no PUT.
        await using (var operador = indexer.NewDbContext(connectionString))
        {
            await operador.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE knowledge_documents
                   SET "ExtractedText" = {Updated}, "ContentRevision" = 2, "IndexingStatus" = 'Pending'
                 WHERE "Id" = {documentId};
                """);
        }

        podeSeguir.SetResult();
        var outcome = await indexing;

        Assert.Equal(KnowledgeIndexingOutcome.Discarded, outcome);

        await using var check = indexer.NewDbContext(connectionString);
        Assert.Empty(await check.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());

        var document = await check.KnowledgeDocuments.AsNoTracking().FirstAsync(d => d.Id == documentId);
        Assert.NotEqual(KnowledgeIndexingStatus.Indexed, document.IndexingStatus);
        Assert.Null(document.IndexedAt);
        Assert.Equal(0, document.FragmentCount);
    }

    /// <summary>
    /// O documento é <b>excluído</b> durante a indexação.
    ///
    /// <para>
    /// Este é o caso que a verificação V4 corrigiu na D7 da etapa 1: aquela
    /// decisão afirmava que <c>ContentRevision</c> "cobre o delete de graça".
    /// Cobre o <b>descarte</b> — a atualização condicionada afeta <b>zero
    /// linhas</b>, e é isso que o código checa —, mas não é ela que impede
    /// fragmento órfão: isso é a chave estrangeira em <c>Cascade</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DocumentDeletedDuringIndexing_DiscardsByZeroRowsAffected()
    {
        var connectionString = fixture.Postgres.GetConnectionString();
        var indexer = KnowledgeIndexingHarness.Build(connectionString);
        await using var seeding = indexer.NewDbContext(connectionString);
        var (_, documentId) = await KnowledgeIndexingHarness.SeedDocumentAsync(seeding, Original);

        var chegouNoProvedor = new TaskCompletionSource();
        var podeSeguir = new TaskCompletionSource();
        indexer.Embeddings.BeforeGenerate = async () =>
        {
            chegouNoProvedor.TrySetResult();
            await podeSeguir.Task;
        };

        var indexing = indexer.Service.IndexAsync(new KnowledgeIndexingJobMessage(documentId, 1), default);
        await chegouNoProvedor.Task;

        await using (var operador = indexer.NewDbContext(connectionString))
        {
            await operador.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM knowledge_documents WHERE "Id" = {documentId};""");
        }

        podeSeguir.SetResult();
        var outcome = await indexing;

        // Descartado, e não "falhou": o documento sumir não é erro de indexação.
        Assert.Equal(KnowledgeIndexingOutcome.Discarded, outcome);

        await using var check = indexer.NewDbContext(connectionString);
        Assert.Empty(await check.KnowledgeFragments.Where(f => f.KnowledgeDocumentId == documentId).ToListAsync());
        Assert.False(await check.KnowledgeDocuments.AnyAsync(d => d.Id == documentId));
    }
}
