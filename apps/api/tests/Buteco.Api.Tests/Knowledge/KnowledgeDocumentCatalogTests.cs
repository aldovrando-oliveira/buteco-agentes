using System.Net;
using System.Net.Http.Json;
using System.Text;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Options;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

public class KnowledgeDocumentCatalogTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // --- Ordenação (api-response-ordering) ---------------------------------

    [Fact]
    public async Task KnowledgeDocumentsWithEqualCreatedAt_AreTieBrokenById()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Empate Docs");
        var first = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc Empate 1");
        var second = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc Empate 2");
        var third = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc Empate 3");

        var tied = new[] { first.Id, second.Id, third.Id };
        await CreatedAtTie.ForceAsync(factory.Services, "knowledge_documents", tied);

        var response = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents");
        response.EnsureSuccessStatusCode();
        var listed = (await response.Content.ReadFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>())!;

        var observed = listed.Where(d => tied.Contains(d.Id)).Select(d => d.Id).ToList();
        Assert.Equal(tied.Order().ToList(), observed);
    }

    // Metade determinística (design.md, D6).
    [Fact]
    public async Task KnowledgeDocumentCatalogQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base SQL Docs");
        await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc SQL");

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            (await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents")).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM knowledge_documents", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    [Fact]
    public async Task CreateDocument_WithMarkdown_ReturnsPendingDocument()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Política de trocas", "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.NotNull(document);
        Assert.Equal(knowledgeBase.Id, document.KnowledgeBaseId);
        Assert.Equal("Política de trocas", document.Title);
        Assert.Equal("markdown", document.SourceType);
        Assert.Equal(KnowledgeIndexingStatus.Pending, document.IndexingStatus);
        Assert.Null(document.IndexedAt);
        Assert.Null(document.FailureReason);
        Assert.Equal(1, document.ContentRevision);
    }

    [Fact]
    public async Task CreateDocument_InMissingBase_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{Guid.NewGuid()}/documents",
            new CreateKnowledgeDocumentRequest("Título", "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Desativar a base impede o uso pelo agente, não a manutenção do conteúdo.
    [Fact]
    public async Task CreateDocument_InInactiveBase_IsAllowed()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base inativa");
        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null);

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Documento em base inativa", "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateDocument_WithoutTitle_ReturnsValidationProblem(string? title)
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest(title, "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    public async Task CreateDocument_WithEmptyContent_ReturnsValidationProblem(string? content)
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Título", "markdown", content));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDocument_WithDuplicateTitleInSameBase_CreatesBoth()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var first = await _client.CreateDocumentAsync(knowledgeBase.Id, "Título repetido");
        var second = await _client.CreateDocumentAsync(knowledgeBase.Id, "Título repetido");

        Assert.NotEqual(first.Id, second.Id);
    }

    // Tipo de origem sem extrator registrado (design.md, D12).
    [Fact]
    public async Task CreateDocument_WithUnknownSourceType_ReturnsValidationProblemListingSupported()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Título", "pdf", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("markdown", body);
    }

    [Fact]
    public async Task CreateDocument_AtExactlyTheSizeCap_IsAccepted()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var content = new string('a', KnowledgeDocumentLimits.MaxContentBytes);

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("No limite", "markdown", content));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(KnowledgeDocumentLimits.MaxContentBytes, document!.ContentLengthBytes);
    }

    [Fact]
    public async Task CreateDocument_AboveTheSizeCap_ReturnsValidationProblemAndCreatesNothing()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var content = new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1);

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Acima do limite", "markdown", content));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var documents = await _client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents");
        Assert.Empty(documents!);
    }

    // O teto mede o texto JÁ EXTRAÍDO, não a entrada crua (design.md, D5/R12).
    // BOM + CRLF encolhem o conteúdo: este payload passa do teto em bruto e
    // cabe depois de extraído.
    [Fact]
    public async Task CreateDocument_WhenRawExceedsCapButExtractedDoesNot_IsAccepted()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var lines = new StringBuilder("﻿");
        for (var i = 0; i < 57_000; i++)
        {
            lines.Append("linha de conteudo\r\n");
        }

        var raw = lines.ToString();
        var extracted = raw.TrimStart('﻿').Replace("\r\n", "\n");

        Assert.True(Encoding.UTF8.GetByteCount(raw) > KnowledgeDocumentLimits.MaxContentBytes,
            "o payload cru precisa passar do teto para este cenário fazer sentido");
        Assert.True(Encoding.UTF8.GetByteCount(extracted) <= KnowledgeDocumentLimits.MaxContentBytes,
            "o texto extraído precisa caber no teto para este cenário fazer sentido");

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("BOM e CRLF", "markdown", raw));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(Encoding.UTF8.GetByteCount(extracted), document!.ContentLengthBytes);
    }

    [Fact]
    public async Task ListDocuments_ReturnsSummariesWithoutContent()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento listado");

        var response = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Asserção negativa: é ela que impede a regressão bem-intencionada de
        // "devolver o conteúdo junto porque é conveniente" (design.md, D14/R7).
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("extractedText", body, StringComparison.OrdinalIgnoreCase);

        var documents = await response.Content.ReadFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>();
        var listed = Assert.Single(documents!);
        Assert.Equal("Documento listado", listed.Title);
        Assert.Equal(KnowledgeIndexingStatus.Pending, listed.IndexingStatus);
    }

    [Fact]
    public async Task ListDocuments_ForBaseWithoutDocuments_ReturnsEmptyList()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base vazia");

        var response = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>())!);
    }

    [Fact]
    public async Task ListDocuments_ForMissingBase_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/knowledge-bases/{Guid.NewGuid()}/documents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentById_IncludesContentAndSummaryFields()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento completo");

        var response = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("extractedText", body, StringComparison.OrdinalIgnoreCase);

        var document = await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>();
        Assert.Equal(KnowledgeTestClient.SampleMarkdown, document!.ExtractedText);
        Assert.Equal(Encoding.UTF8.GetByteCount(KnowledgeTestClient.SampleMarkdown), document.ContentLengthBytes);
    }

    [Fact]
    public async Task GetDocumentById_WhenMissing_ReturnsNotFound()
    {
        var knowledgeBase = await _client.CreateBaseAsync();

        var response = await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Documento existe, mas pertence a outra base: o caminho errado responde
    // 404 porque o handler filtra por KnowledgeBaseId E Id.
    [Fact]
    public async Task GetDocumentById_ThroughWrongBase_ReturnsNotFound()
    {
        var owner = await _client.CreateBaseAsync("Base dona");
        var other = await _client.CreateBaseAsync("Outra base");
        var document = await _client.CreateDocumentAsync(owner.Id, "Documento da base dona");

        var response = await _client.GetAsync($"/knowledge-bases/{other.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Estado esperado desta etapa: não há fila nem consumidor, então o
    // documento nasce Pending e permanece Pending (design.md, D10).
    [Fact]
    public async Task Document_StaysPending_WithoutAnyIndexingConsumer()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento parado");

        Assert.Equal(KnowledgeIndexingStatus.Pending, created.IndexingStatus);

        var reread = await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{created.Id}");

        Assert.Equal(KnowledgeIndexingStatus.Pending, reread!.IndexingStatus);
        Assert.Null(reread.IndexedAt);
    }

    // Bytes UTF-8, não caracteres: em português acentuado os dois divergem, e a
    // unidade exposta tem de ser a mesma que a validação usa (design.md, D5/D14).
    [Fact]
    public async Task ContentLengthBytes_IsMeasuredInUtf8Bytes_NotCharacters()
    {
        var knowledgeBase = await _client.CreateBaseAsync();
        const string acentuado = "# Política de trocas\n\nNão há reembolso após 30 dias. 😀\n";

        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Acentuado", acentuado);

        var bytes = Encoding.UTF8.GetByteCount(acentuado);
        Assert.NotEqual(acentuado.Length, bytes);
        Assert.Equal(bytes, created.ContentLengthBytes);
    }
}
