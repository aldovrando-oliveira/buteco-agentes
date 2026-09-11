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
/// A rota de reindexação: a <b>única</b> entrada que reenfileira indexação sem o
/// conteúdo ter mudado.
///
/// <para>
/// Os estados terminais são forçados direto no banco (<c>ForceFailedAsync</c>,
/// <c>ForceIndexedAsync</c>) porque <c>apps/api</c> nunca os escreve — quem
/// escreve é o consumidor de <c>apps/workers</c>, que esta change não toca.
/// </para>
/// </summary>
public class KnowledgeDocumentReindexTests(ApiFactoryFixture fixture) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task ReindexingFailedDocument_ReturnsToPending_AndEnqueues()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex falha");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento que falhou");
        await KnowledgeTestClient.ForceFailedAsync(fixture.Services, document.Id);

        var before = fixture.IndexingPublisher.PublishedFor(document.Id).Count;

        var response = await client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}/reindex", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reindexed = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;

        Assert.Equal(KnowledgeIndexingStatus.Pending, reindexed.IndexingStatus);
        Assert.Null(reindexed.FailureReason);
        Assert.Equal(0, reindexed.IndexingAttempts);
        Assert.Null(reindexed.LastAttemptAt);

        // Exatamente UMA mensagem nova, com a contagem de execução reaberta em 1
        // — é a abertura de uma rodada, não a continuação da anterior.
        var published = fixture.IndexingPublisher.PublishedFor(document.Id);
        Assert.Equal(before + 1, published.Count);
        Assert.Equal(1, published.Last().Attempt);
    }

    /// <summary>
    /// Garantia 3 de D9 da etapa 1: o conteúdo anterior continua respondendo até
    /// os fragmentos novos serem gravados. E é <c>IndexedAt</c> não nulo que
    /// mantém <c>FragmentCount</c> exibível pela regra da etapa 1 — zerar aqui
    /// apagaria a distinção que a tela usa.
    /// </summary>
    [Fact]
    public async Task Reindexing_PreservesIndexedAtAndFragmentCount()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex preserva");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento com índice anterior");
        await KnowledgeTestClient.ForceFailedAsync(fixture.Services, document.Id, withPreviousIndexing: true);

        var failed = await client.GetFromJsonAsync<KnowledgeDocumentResponse>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");
        Assert.NotNull(failed!.IndexedAt);
        Assert.Equal(12, failed.FragmentCount);

        var reindexed = await client.ReindexDocumentAsync(knowledgeBase.Id, document.Id);

        Assert.Equal(KnowledgeIndexingStatus.Pending, reindexed.IndexingStatus);
        Assert.Equal(failed.IndexedAt, reindexed.IndexedAt);
        Assert.Equal(12, reindexed.FragmentCount);
    }

    /// <summary>
    /// <c>ContentRevision</c> é o token de descarte do consumidor e move-se com o
    /// texto; reindexar não muda texto nenhum. Incrementá-la descartaria uma
    /// indexação em voo do mesmo conteúdo.
    /// </summary>
    [Fact]
    public async Task Reindexing_DoesNotChangeContentRevision()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex revisão");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento revisão");

        var reindexed = await client.ReindexDocumentAsync(knowledgeBase.Id, document.Id);

        Assert.Equal(document.ContentRevision, reindexed.ContentRevision);
        Assert.Equal(document.ContentRevision, fixture.IndexingPublisher.PublishedFor(document.Id).Last().ContentRevision);
    }

    /// <summary>
    /// <b>O guarda de R3, e o mais delicado dos cinco.</b> Anular
    /// <c>ContentHash</c> em <c>RequestReindex()</c> pareceria inofensivo — força
    /// o reprocessamento, resolve o problema imediato e passa numa revisão
    /// apressada. O que ele quebra é o significado de hash nulo fixado pela 2a
    /// ("linha legada, nunca indexada sob esta regra") e, aqui, faz a
    /// <b>próxima</b> atualização só de título reindexar sem motivo.
    ///
    /// <para>
    /// A asserção é <b>negativa</b> e vive no cenário de reindexação, não junto
    /// dos cenários de "conteúdo idêntico não é reindexado" da 2a — aqueles
    /// afirmam outra coisa e continuariam verdes com o defeito presente
    /// (convenção 15, segunda forma: guarda no componente errado).
    /// </para>
    /// </summary>
    [Fact]
    public async Task AfterReindexing_UpdateWithSameContent_StillDoesNotEnqueue()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex hash");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento hash");

        await client.ReindexDocumentAsync(knowledgeBase.Id, document.Id);
        var afterReindex = fixture.IndexingPublisher.PublishedFor(document.Id).Count;

        // Mesmo conteúdo, título diferente: pela regra do ContentHash, não
        // enfileira. Com ContentHash anulado pela reindexação, enfileiraria.
        var update = await client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Título trocado", "markdown", KnowledgeTestClient.SampleMarkdown));
        update.EnsureSuccessStatusCode();

        Assert.Equal(afterReindex, fixture.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    [Fact]
    public async Task ReindexingNeverIndexedDocument_StaysPending_AndEnqueues()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex nunca indexado");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento nunca indexado");

        Assert.Equal(KnowledgeIndexingStatus.Pending, document.IndexingStatus);
        Assert.Null(document.IndexedAt);

        var before = fixture.IndexingPublisher.PublishedFor(document.Id).Count;
        var reindexed = await client.ReindexDocumentAsync(knowledgeBase.Id, document.Id);

        Assert.Equal(KnowledgeIndexingStatus.Pending, reindexed.IndexingStatus);
        Assert.Null(reindexed.IndexedAt);
        Assert.Equal(before + 1, fixture.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    /// <summary>
    /// Desativar a base impede o uso pelo agente, não a manutenção do conteúdo —
    /// mesma regra que o cadastro de documento já enuncia. Reindexar antes de
    /// reativar é exatamente o que um operador faz.
    /// </summary>
    [Fact]
    public async Task ReindexingDocumentOfInactiveBase_IsAccepted()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex inativa");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento de base inativa");

        (await client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", content: null))
            .EnsureSuccessStatusCode();

        var before = fixture.IndexingPublisher.PublishedFor(document.Id).Count;

        var response = await client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}/reindex", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before + 1, fixture.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    [Fact]
    public async Task ReindexingDocumentOfAnotherBase_ReturnsNotFound_AndPublishesNothing()
    {
        var client = fixture.CreateClient();
        var owner = await client.CreateBaseAsync("Base dona do documento");
        var other = await client.CreateBaseAsync("Base alheia");
        var document = await client.CreateDocumentAsync(owner.Id, "Documento da base dona");

        var before = fixture.IndexingPublisher.PublishedFor(document.Id).Count;

        var response = await client.PostAsync(
            $"/knowledge-bases/{other.Id}/documents/{document.Id}/reindex", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, fixture.IndexingPublisher.PublishedFor(document.Id).Count);

        // E o documento continua exatamente como estava na base dele.
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await dbContext.KnowledgeDocuments.SingleAsync(d => d.Id == document.Id);
        Assert.Equal(document.ContentRevision, stored.ContentRevision);
    }

    [Fact]
    public async Task ReindexingUnknownDocument_ReturnsNotFound_AndPublishesNothing()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base reindex inexistente");
        var unknown = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{unknown}/reindex", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(fixture.IndexingPublisher.PublishedFor(unknown));
    }
}
