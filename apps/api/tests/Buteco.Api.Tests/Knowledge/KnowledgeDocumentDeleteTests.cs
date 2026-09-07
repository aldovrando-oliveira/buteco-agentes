using System.Net;
using System.Net.Http.Json;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;

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
}
