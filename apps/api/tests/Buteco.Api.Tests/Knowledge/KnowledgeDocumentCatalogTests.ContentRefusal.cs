using System.Net;
using System.Text;
using System.Text.Json;
using Buteco.Api.KnowledgeDocuments.Options;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Código na recusa de conteúdo (design.md da change codigo-recusa-conteudo-upsert).
/// Toda asserção lê o TEXTO da resposta com <see cref="JsonDocument"/>, nunca um tipo
/// do apps/api (convenção 12): o que a #105 vai ler é o fio. Os corpos também saem
/// como JSON cru, para poder omitir um campo, que é a recusa de forma.
/// <c>partial</c> da classe existente, para não subir outro contêiner.
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    private static readonly string[] ContentRefusalCodes =
        ["too-large", "unsupported-source-type", "null-character", "empty-content"];

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task AssertContentRefusalAsync(HttpResponseMessage response, string expectedCode)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadBodyAsync(response);
        Assert.True(body.TryGetProperty("code", out var code), $"Sem a propriedade code no corpo: {body}");
        Assert.Equal(expectedCode, code.GetString());
    }

    private static async Task AssertShapeRefusalAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), $"Sem errors.{field}: {text}");
        Assert.False(body.TryGetProperty("code", out _), $"Recusa de forma com code: {text}");
        foreach (var contentCode in ContentRefusalCodes)
        {
            Assert.DoesNotContain(contentCode, text);
        }
    }

    // --- Upsert de /sync ----------------------------------------------------

    [Fact]
    public async Task Upsert_AboveTheSizeCap_RespondsTooLargeOnTheWireAndKeepsTheDocument()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-teto", "v1");
        var before = (await FindByRefAsync(knowledgeBase.Id, "ref-teto"))!;
        var content = new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1);

        var response = await Connectors.UpsertAsync(knowledgeBase.Id, "ref-teto", "v2", content: content);

        await AssertContentRefusalAsync(response, "too-large");
        var body = await ReadBodyAsync(response);
        Assert.Equal(KnowledgeDocumentLimits.MaxContentBytes + 1, body.GetProperty("contentBytes").GetInt32());
        Assert.Equal(KnowledgeDocumentLimits.MaxContentBytes, body.GetProperty("maxContentBytes").GetInt32());
        Assert.Contains(KnowledgeDocumentLimits.MaxContentBytes.ToString(), body.GetProperty("detail").GetString());

        var after = (await FindByRefAsync(knowledgeBase.Id, "ref-teto"))!;
        Assert.Equal("v1", after.ExternalVersion);
        Assert.Equal(before.ContentRevision, after.ContentRevision);
        Assert.Equal(before.ExtractedText, after.ExtractedText);
    }

    [Theory]
    [InlineData("pdf", "# Título\n\ntexto\n", "unsupported-source-type")]
    [InlineData("markdown", "# Título\n\ncom \u0000 nulo\n", "null-character")]
    [InlineData("markdown", "", "empty-content")]
    [InlineData("markdown", "   \n\t  ", "empty-content")]
    [InlineData("markdown", "﻿", "empty-content")]
    public async Task Upsert_OtherContentRefusals_RespondTheirCode(string sourceType, string content, string expectedCode)
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.PutAsync(
            KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id),
            Json(new { externalRef = "ref-recusa", externalVersion = "v1", title = "Documento", sourceType, content }));

        await AssertContentRefusalAsync(response, expectedCode);
        Assert.Equal(0, await CountByRefAsync(knowledgeBase.Id, "ref-recusa"));
    }

    [Fact]
    public async Task Upsert_WithoutExternalRef_IsShapeRefusalWithoutCode()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.PutAsync(
            KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id),
            Json(new { externalVersion = "v1", title = "Documento", sourceType = "markdown", content = KnowledgeTestClient.SampleMarkdown }));

        await AssertShapeRefusalAsync(response, "externalRef");
    }

    // `content` AUSENTE continua forma; só o presente e em branco virou conteúdo (D2).
    [Fact]
    public async Task Upsert_WithoutContentField_IsShapeRefusalWithoutCode()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.PutAsync(
            KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id),
            Json(new { externalRef = "ref-sem-conteudo", externalVersion = "v1", title = "Documento", sourceType = "markdown" }));

        await AssertShapeRefusalAsync(response, "content");
        Assert.Equal(0, await CountByRefAsync(knowledgeBase.Id, "ref-sem-conteudo"));
    }

    [Fact]
    public async Task Upsert_ExactlyAtTheSizeCap_IsCreated()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var content = new string('a', KnowledgeDocumentLimits.MaxContentBytes);

        var response = await Connectors.UpsertAsync(knowledgeBase.Id, "ref-no-teto", content: content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadBodyAsync(response);
        Assert.Equal("Created", body.GetProperty("outcome").GetString());
    }

    // --- Rotas do operador (D4) ---------------------------------------------

    [Fact]
    public async Task CreateDocument_AboveTheSizeCap_KeepsErrorsAndTitleAndAddsTheCode()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Código Teto");

        var response = await _client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            Json(new { title = "Acima do limite", sourceType = "markdown", content = new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1) }));

        await AssertContentRefusalAsync(response, "too-large");
        var body = await ReadBodyAsync(response);
        Assert.Single(body.GetProperty("errors").GetProperty("content").EnumerateArray());
        Assert.Equal("One or more validation errors occurred.", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CreateDocument_WithUnknownSourceType_RespondsUnsupportedSourceType()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Código Tipo");

        var response = await _client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            Json(new { title = "Documento", sourceType = "pdf", content = KnowledgeTestClient.SampleMarkdown }));

        await AssertContentRefusalAsync(response, "unsupported-source-type");
        var body = await ReadBodyAsync(response);
        Assert.True(body.GetProperty("errors").TryGetProperty("sourceType", out _));
    }

    [Fact]
    public async Task UpdateDocument_WithNulCharacter_RespondsNullCharacterAndKeepsTheDocument()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Código Nulo");
        var created = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");

        var response = await _client.PutAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{created.Id}",
            Json(new { title = "Documento", sourceType = "markdown", content = "# Título\n\ncom \u0000 nulo\n" }));

        await AssertContentRefusalAsync(response, "null-character");
        var current = await _client.GetStringAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{created.Id}");
        var document = JsonDocument.Parse(current).RootElement;
        Assert.Equal(created.ContentRevision, document.GetProperty("contentRevision").GetInt32());
        Assert.Equal(KnowledgeTestClient.SampleMarkdown, document.GetProperty("extractedText").GetString());
    }

    [Fact]
    public async Task CreateDocument_WithoutTitle_IsShapeRefusalWithoutCode()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Código Forma");

        var response = await _client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            Json(new { sourceType = "markdown", content = KnowledgeTestClient.SampleMarkdown }));

        await AssertShapeRefusalAsync(response, "title");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("﻿")]
    public async Task CreateDocument_WithBlankContent_RespondsEmptyContent(string content)
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Código Vazio");

        var response = await _client.PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            Json(new { title = "Documento", sourceType = "markdown", content }));

        await AssertContentRefusalAsync(response, "empty-content");
    }

    // Efeito da D2: conteúdo em branco é recusado DEPOIS de procurar a base.
    [Fact]
    public async Task CreateDocument_WithBlankContentInMissingBase_Returns404()
    {
        var response = await _client.PostAsync(
            $"/knowledge-bases/{Guid.NewGuid()}/documents",
            Json(new { title = "Documento", sourceType = "markdown", content = "   " }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
