using System.Net;
using System.Net.Http.Json;
using System.Text;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Options;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

    // --- Histórico de documentos (historico-documentos-base) ---------------

    private async Task<KnowledgeDocumentEventResponse> SingleUpdatedEventAsync(Guid knowledgeBaseId)
    {
        var events = await _client.GetAllDocumentEventsAsync(knowledgeBaseId);
        return Assert.Single(events, documentEvent => documentEvent.Type == KnowledgeDocumentEventType.Updated);
    }

    [Fact]
    public async Task UpdateDocument_WithOnlyNewContent_RecordsUpdatedWithContentChanged()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        (await PutAsync(knowledgeBase.Id, created.Id, "Documento", "# Novo\n\nOutro texto.\n")).EnsureSuccessStatusCode();

        var updated = await SingleUpdatedEventAsync(knowledgeBase.Id);
        Assert.Equal(created.Id, updated.DocumentId);
        Assert.True(updated.ContentChanged);
        Assert.False(updated.TitleChanged);
    }

    [Fact]
    public async Task UpdateDocument_WithOnlyNewTitle_RecordsUpdatedWithTitleChangedAndNewTitle()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Título antigo");

        (await PutAsync(knowledgeBase.Id, created.Id, "Título novo", KnowledgeTestClient.SampleMarkdown)).EnsureSuccessStatusCode();

        var updated = await SingleUpdatedEventAsync(knowledgeBase.Id);
        Assert.False(updated.ContentChanged);
        Assert.True(updated.TitleChanged);
        Assert.Equal("Título novo", updated.DocumentTitle);
    }

    // Uma escrita, um evento — não um por campo alterado (D5).
    [Fact]
    public async Task UpdateDocument_WithNewContentAndTitle_RecordsASingleUpdatedWithBothFlags()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Antigo");

        (await PutAsync(knowledgeBase.Id, created.Id, "Novo", "# Novo\n\nOutro texto.\n")).EnsureSuccessStatusCode();

        var updated = await SingleUpdatedEventAsync(knowledgeBase.Id);
        Assert.True(updated.ContentChanged);
        Assert.True(updated.TitleChanged);
        Assert.Equal(2, (await _client.GetAllDocumentEventsAsync(knowledgeBase.Id)).Count);
    }

    [Fact]
    public async Task UpdateDocument_WithIdenticalContentAndTitle_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Documento", KnowledgeTestClient.SampleMarkdown);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var only = Assert.Single(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
        Assert.Equal(KnowledgeDocumentEventType.Created, only.Type);
    }

    [Fact]
    public async Task UpdateDocument_WithInvalidContent_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(knowledgeBase.Id, created.Id, "Título novo", "# Título\n\ncom \u0000 nulo");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task UpdateDocument_AboveTheSizeCap_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await PutAsync(
            knowledgeBase.Id, created.Id, "Título novo", new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task UpdateDocument_WhenMissing_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await PutAsync(knowledgeBase.Id, Guid.NewGuid(), "Título", "# Outro\n");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    // Pelo caminho errado: nem a base do caminho nem a dona ganham evento.
    [Fact]
    public async Task UpdateDocument_ThroughWrongBase_RecordsNoEventInEitherBase()
    {
        var owner = await _client.CreateBaseAsync("Base dona");
        var other = await _client.CreateBaseAsync("Outra base");
        var document = await _client.CreateDocumentAsync(owner.Id, "Documento protegido");

        var response = await PutAsync(other.Id, document.Id, "Título invasor", "# Conteúdo invasor\n");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(other.Id));
        var ownerEvent = Assert.Single(await _client.GetAllDocumentEventsAsync(owner.Id));
        Assert.Equal(KnowledgeDocumentEventType.Created, ownerEvent.Type);
    }

    // A CHECK do banco torna impossível o evento que a D4 existe para evitar
    // (D5). Inserção direta, contornando a fábrica da entidade de propósito.
    [Fact]
    public async Task DocumentEventsTable_RejectsUpdatedWithoutAnyChange()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO knowledge_document_events
                ("Id", "KnowledgeBaseId", "DocumentId", "DocumentTitle", "Type", "ContentChanged", "TitleChanged", "Author", "OccurredAt")
            VALUES ({Guid.NewGuid()}, {knowledgeBase.Id}, {Guid.NewGuid()}, 'Documento', 'Updated', FALSE, FALSE, 'operator', {DateTimeOffset.UtcNow});
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_document_events_change_detail", exception.ConstraintName);
    }
}
