using System.Net;
using System.Net.Http.Json;
using System.Text;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Options;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

public class KnowledgeDocumentUpdateTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PutAsync(Guid baseId, Guid documentId, string title, string content) =>
        _client.PutAsJsonAsync(
            $"/knowledge-bases/{baseId}/documents/{documentId}",
            new UpdateKnowledgeDocumentRequest(title, "markdown", content));

    [Fact]
    public async Task UpdateDocument_WithDifferentContent_IncrementsRevisionAndReturnsToPending()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Documento", "# Conteúdo novo\n\nOutro texto.\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(created.ContentRevision + 1, updated!.ContentRevision);
        Assert.Equal(KnowledgeIndexingStatus.Pending, updated.IndexingStatus);
        Assert.Null(updated.FailureReason);
    }

    // Só o título mudou: a revisão de CONTEÚDO não pode andar, senão a etapa de
    // indexação descartaria trabalho em andamento à toa (design.md, D7).
    [Fact]
    public async Task UpdateDocument_WithOnlyNewTitle_DoesNotIncrementRevision()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Título antigo");

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Título novo", KnowledgeTestClient.SampleMarkdown);

        var updated = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal("Título novo", updated!.Title);
        Assert.Equal(created.ContentRevision, updated.ContentRevision);
    }

    [Fact]
    public async Task UpdateDocument_WithIdenticalContent_DoesNotIncrementRevision()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Documento", KnowledgeTestClient.SampleMarkdown);

        var updated = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(created.ContentRevision, updated!.ContentRevision);
    }

    // A coluna gerada acompanha o texto novo sem Reload() explícito — a
    // garantia é do banco, não da aplicação (design.md, D14).
    [Fact]
    public async Task UpdateDocument_RecalculatesContentLengthBytes()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");
        const string novo = "# Muito maior\n\nNão há reembolso após 30 dias, exceto por defeito de fábrica. 😀\n";

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Documento", novo);

        var updated = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(Encoding.UTF8.GetByteCount(novo), updated!.ContentLengthBytes);
        Assert.NotEqual(created.ContentLengthBytes, updated.ContentLengthBytes);
    }

    [Fact]
    public async Task UpdateDocument_WhenMissing_ReturnsNotFound()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await PutAsync(knowledgeBase.Id, Guid.NewGuid(), "Título", KnowledgeTestClient.SampleMarkdown);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Documento de outra base pelo caminho errado: 404, e o documento da outra
    // base fica intacto.
    [Fact]
    public async Task UpdateDocument_ThroughWrongBase_ReturnsNotFoundAndLeavesDocumentUntouched()
    {
        var owner = await _client.CreateBaseAsync("Base dona");
        var other = await _client.CreateBaseAsync("Outra base");
        var document = await _client.CreateDocumentAsync(owner.Id, "Documento protegido");

        var response = await PutAsync(other.Id, document.Id, "Título invasor", "# Conteúdo invasor\n");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var reread = await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(
            $"/knowledge-bases/{owner.Id}/documents/{document.Id}");
        Assert.Equal("Documento protegido", reread!.Title);
        Assert.Equal(KnowledgeTestClient.SampleMarkdown, reread.ExtractedText);
        Assert.Equal(document.ContentRevision, reread.ContentRevision);
    }

    [Fact]
    public async Task UpdateDocument_InInactiveBase_IsAllowed()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base a desativar");
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");
        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null);

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Título atualizado", "# Conteúdo atualizado\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateDocument_AboveTheSizeCap_LeavesContentAndRevisionIntact()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(
            knowledgeBase.Id, created.Id, "Documento", new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var reread = await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{created.Id}");
        Assert.Equal(KnowledgeTestClient.SampleMarkdown, reread!.ExtractedText);
        Assert.Equal(created.ContentRevision, reread.ContentRevision);
    }
}
