using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Exclusão real — a única do repositório (design.md, D6).
/// </summary>
public class KnowledgeDocumentDeleteTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DeleteDocument_WhenExists_RemovesItFromListAndLookup()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento a excluir");

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var lookup = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);

        var documents = await _client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents");
        Assert.DoesNotContain(documents!, item => item.Id == document.Id);
    }

    [Fact]
    public async Task DeleteDocument_WhenMissing_ReturnsNotFound()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // O cenário mais caro de errar da change: a exclusão é irreversível, e
    // resolver o documento só por Id permitiria apagar o de outra base pelo
    // caminho errado.
    [Fact]
    public async Task DeleteDocument_ThroughWrongBase_ReturnsNotFoundAndDocumentSurvives()
    {
        var owner = await _client.CreateBaseAsync("Base dona");
        var other = await _client.CreateBaseAsync("Outra base");
        var document = await _client.CreateDocumentAsync(owner.Id, "Documento protegido");

        var response = await _client.DeleteAsync($"/knowledge-bases/{other.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var reread = await _client.GetAsync($"/knowledge-bases/{owner.Id}/documents/{document.Id}");
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
    }

    [Fact]
    public async Task DeleteDocument_InInactiveBase_IsAllowed()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base inativa");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");
        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null);

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDocument_DoesNotAffectOtherDocuments()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var toDelete = await _client.CreateDocumentAsync(knowledgeBase.Id, "Some");
        var toKeep = await _client.CreateDocumentAsync(knowledgeBase.Id, "Fica");

        await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{toDelete.Id}");

        var documents = await _client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents");
        var remaining = Assert.Single(documents!);
        Assert.Equal(toKeep.Id, remaining.Id);
    }

    // Excluir o último documento não toca a base: ela continua existindo com
    // listagem vazia (a FK é Restrict, e a base não tem exclusão).
    [Fact]
    public async Task DeleteLastDocument_LeavesBaseWithEmptyList()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base que sobrevive");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Único documento");

        await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        var baseResponse = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}");
        Assert.Equal(HttpStatusCode.OK, baseResponse.StatusCode);
        Assert.NotNull(await baseResponse.Content.ReadFromJsonAsync<KnowledgeBaseResponse>());

        var documents = await _client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents");
        Assert.Empty(documents!);
    }

    // --- Histórico de documentos (historico-documentos-base) ---------------

    [Fact]
    public async Task DeleteDocument_RecordsDeletedEventThatOutlivesTheDocument()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Antigo");
        (await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Contrato 2025", "markdown", KnowledgeTestClient.SampleMarkdown))).EnsureSuccessStatusCode();

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var lookup = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);

        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);
        Assert.Equal(
            new[] { KnowledgeDocumentEventType.Deleted, KnowledgeDocumentEventType.Updated, KnowledgeDocumentEventType.Created },
            events.Select(documentEvent => documentEvent.Type));
        Assert.All(events, documentEvent => Assert.Equal(document.Id, documentEvent.DocumentId));

        var deleted = events[0];
        Assert.Equal("Contrato 2025", deleted.DocumentTitle);
        Assert.Null(deleted.ContentChanged);
        Assert.Null(deleted.TitleChanged);
    }

    [Fact]
    public async Task DeleteDocument_WhenMissing_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task DeleteDocument_ThroughWrongBase_RecordsNoEventInEitherBase()
    {
        var owner = await _client.CreateBaseAsync("Base dona");
        var other = await _client.CreateBaseAsync("Outra base");
        var document = await _client.CreateDocumentAsync(owner.Id, "Documento protegido");

        var response = await _client.DeleteAsync($"/knowledge-bases/{other.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(other.Id));
        var ownerEvent = Assert.Single(await _client.GetAllDocumentEventsAsync(owner.Id));
        Assert.Equal(KnowledgeDocumentEventType.Created, ownerEvent.Type);
    }

    /// <summary>
    /// A cascata da D3, exercida no único lugar em que ela mora hoje: o banco. A
    /// base não tem rota de exclusão (#108), e com documento vivo o Restrict de
    /// knowledge_documents barra até o SQL direto — por isso a base é esvaziada
    /// pela rota antes, e sobram só os eventos.
    /// </summary>
    [Fact]
    public async Task DeletingTheBaseRow_RemovesItsEventsAndOnlyIts()
    {
        var doomed = await _client.CreateBaseAsync("Base a apagar");
        var survivor = await _client.CreateBaseAsync("Base que fica");
        var doomedDocument = await _client.CreateDocumentAsync(doomed.Id, "Some junto");
        await _client.CreateDocumentAsync(survivor.Id, "Fica");
        (await _client.DeleteAsync($"/knowledge-bases/{doomed.Id}/documents/{doomedDocument.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(2, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, doomed.Id));

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM knowledge_bases WHERE "Id" = {doomed.Id};
                """);
        }

        Assert.Equal(0, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, doomed.Id));
        Assert.Equal(1, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, survivor.Id));
    }
}
