using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// O lado de <c>apps/api</c> do contrato de indexação: quem enfileira, quem não
/// enfileira, o que a resposta expõe, e a exclusão com fragmentos gravados.
///
/// <para>
/// Este app <b>não escreve fragmento</b> — quem escreve é <c>apps/workers</c>.
/// Por isso quase tudo aqui é exercitado pela rota HTTP, e o único arranjo que
/// foge disso está marcado no próprio teste, com o motivo.
/// </para>
/// </summary>
public class KnowledgeDocumentIndexingContractTests(ApiFactoryFixture fixture) : IClassFixture<ApiFactoryFixture>
{
    // ------------------------------------------------- enfileira / não ----

    [Fact]
    public async Task CreatingDocument_EnqueuesIndexing()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();

        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        var published = fixture.IndexingPublisher.PublishedFor(document.Id);
        Assert.Single(published);
        Assert.Equal(document.ContentRevision, published.Single().ContentRevision);
    }

    [Fact]
    public async Task UpdatingContent_EnqueuesIndexing_AndGoesBackToPending()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();
        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        var response = await client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Documento", "markdown", "# Outro\n\nConteúdo diferente.\n"));

        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;

        Assert.Equal(KnowledgeIndexingStatus.Pending, updated.IndexingStatus);
        Assert.Equal(document.ContentRevision + 1, updated.ContentRevision);
        Assert.Equal(2, fixture.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    // O par NEGATIVO, que é o que a regra de ContentHash existe para produzir:
    // metadado muda, conteúdo não, nada é enfileirado e nada é gasto em
    // embedding.
    [Fact]
    public async Task UpdatingOnlyTheTitle_DoesNotEnqueue_AndPreservesIndexingState()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();
        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        var response = await client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Título novo", "markdown", KnowledgeTestClient.SampleMarkdown));

        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;

        Assert.Equal("Título novo", updated.Title);
        Assert.Equal(document.ContentRevision, updated.ContentRevision);

        // Uma publicação só: a da criação. O PUT não acrescentou nenhuma.
        Assert.Single(fixture.IndexingPublisher.PublishedFor(document.Id));
    }

    /// <summary>
    /// <b>O documento LEGADO — e este é o único teste do arquivo que grava
    /// direto no banco em vez de usar a rota. O motivo:</b> nenhum <c>POST</c>
    /// produz documento com <c>ContentHash</c> nulo depois desta change, porque
    /// o construtor sempre o calcula. A linha legada só existe no banco de quem
    /// tinha documentos antes dela, e o único jeito de exercitar o caminho é
    /// fabricá-la.
    ///
    /// <para>
    /// O comportamento é o <b>oposto</b> do teste acima, de propósito: conteúdo
    /// idêntico normalmente NÃO enfileira, mas com hash nulo enfileira mesmo
    /// assim. "Nulo" significa "nunca indexado sob esta regra", e tratar essas
    /// linhas como já indexadas as deixaria paradas em <c>Pending</c> para
    /// sempre — que é exatamente o estado que esta change existe para acabar.
    /// </para>
    /// </summary>
    [Fact]
    public async Task LegacyDocumentWithNullHash_IsEnqueuedEvenWithIdenticalContent()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();
        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        // Fabrica o estado legado: hash nulo, como as linhas criadas antes de
        // ContentHash existir.
        using (var scope = fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE knowledge_documents SET "ContentHash" = NULL WHERE "Id" = {document.Id};
                """);
        }

        var publishedBefore = fixture.IndexingPublisher.PublishedFor(document.Id).Count;

        // Conteúdo IDÊNTICO ao que já está gravado.
        var response = await client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Documento", "markdown", KnowledgeTestClient.SampleMarkdown));

        response.EnsureSuccessStatusCode();
        var updated = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;

        Assert.Equal(KnowledgeIndexingStatus.Pending, updated.IndexingStatus);
        Assert.Equal(publishedBefore + 1, fixture.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    // ------------------------------------------------------- resposta ----

    [Fact]
    public async Task NeverIndexedDocument_ReportsNoAttemptAndNoDate()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();

        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        Assert.Null(document.IndexedAt);
        Assert.Equal(0, document.IndexingAttempts);
        Assert.Null(document.LastAttemptAt);
        Assert.Equal(0, document.FragmentCount);
    }

    [Fact]
    public async Task ListingAndDetail_BothExposeTheIndexingFields()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();
        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        var listing = await client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents");
        var detail = await client.GetFromJsonAsync<KnowledgeDocumentResponse>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        var item = Assert.Single(listing!, summary => summary.Id == document.Id);
        Assert.Equal(0, item.FragmentCount);
        Assert.Equal(0, item.IndexingAttempts);
        Assert.Null(item.LastAttemptAt);

        Assert.Equal(0, detail!.FragmentCount);
        Assert.Equal(0, detail.IndexingAttempts);
        Assert.Null(detail.LastAttemptAt);
    }

    // -------------------------------------------------------- exclusão ----

    /// <summary>
    /// <b>O guarda da correção que a verificação V4 fez na D7 da etapa 1, do
    /// lado onde a exclusão mora.</b>
    ///
    /// <para>
    /// A etapa 1 entregou o <b>primeiro <c>MapDelete</c> do repositório</b>, com
    /// exclusão real de documento (D6). Se a chave estrangeira de fragmento para
    /// documento fosse <c>Restrict</c> — o mesmo que documento usa para base —,
    /// excluir um documento <b>indexado</b> passaria a falhar, e a decisão
    /// central da etapa 1 quebraria assim que a indexação existisse.
    /// </para>
    ///
    /// <para>
    /// Este teste vive em <c>apps/api</c> porque é aqui que a exclusão mora, e
    /// fabrica o fragmento direto no banco porque <c>apps/api</c> não escreve
    /// fragmento — quem escreve é <c>apps/workers</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeletingAnIndexedDocument_Succeeds_AndLeavesNoOrphanFragment()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync();
        var document = await client.CreateDocumentAsync(knowledgeBase.Id);

        using (var scope = fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var embedding = "[" + string.Join(',', Enumerable.Repeat("0.1", 4096)) + "]";
            await dbContext.Database.ExecuteSqlRawAsync($"""
                INSERT INTO knowledge_fragments
                  ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "Ordinal", "Text", "Embedding",
                   "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{document.Id}', '{knowledgeBase.Id}', 0, 'trecho',
                        '{embedding}'::vector, 'openai', 'modelo', 4096, now());
                """);
        }

        var response = await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var check = fixture.Services.CreateScope();
        var verification = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verification.KnowledgeDocuments.AnyAsync(d => d.Id == document.Id));
        Assert.False(await verification.KnowledgeFragments.AnyAsync(f => f.KnowledgeDocumentId == document.Id));
    }
}
