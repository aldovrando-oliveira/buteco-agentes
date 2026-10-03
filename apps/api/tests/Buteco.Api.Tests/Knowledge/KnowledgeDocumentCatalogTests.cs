using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Buteco.Api.Auth.Requests;
using Buteco.Api.Auth.Responses;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Options;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

public partial class KnowledgeDocumentCatalogTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
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

    // --- Histórico de documentos (historico-documentos-base) ---------------

    [Fact]
    public async Task CreateDocument_RecordsExactlyOneCreatedEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Cadastro");

        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Política de trocas");

        var documentEvent = Assert.Single(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
        Assert.Equal(KnowledgeDocumentEventType.Created, documentEvent.Type);
        Assert.Equal(document.Id, documentEvent.DocumentId);
        Assert.Equal("Política de trocas", documentEvent.DocumentTitle);
        Assert.Equal("operator", documentEvent.Author);
        Assert.Null(documentEvent.ContentChanged);
        Assert.Null(documentEvent.TitleChanged);
    }

    // Conteúdo que passa a validação de forma do endpoint e é recusado pelo
    // extrator dentro do handler: o caractere NUL. Desde a change
    // codigo-recusa-conteudo-upsert (D2), o conteúdo vazio também chega ao handler;
    // só o conteúdo AUSENTE para no endpoint.
    [Fact]
    public async Task CreateDocument_WithInvalidContent_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Inválido");

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Documento", "markdown", "# Título\n\ncom \u0000 nulo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task CreateDocument_AboveTheSizeCap_RecordsNoEvent()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Teto");

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest(
                "Documento", "markdown", new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _client.GetAllDocumentEventsAsync(knowledgeBase.Id));
    }

    // A rota responde 404 para base inexistente, então ela não serve de
    // testemunha: a contagem vai direto no banco.
    [Fact]
    public async Task CreateDocument_InMissingBase_RecordsNoEvent()
    {
        var missingBaseId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{missingBaseId}/documents",
            new CreateKnowledgeDocumentRequest("Documento", "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, missingBaseId));
    }

    // O token vem do LOGIN de verdade, não do atalho que a fixture anexa: o que
    // se afirma é que o subject que o esquema de autenticação emite é o que o
    // endpoint lê (D6). Com a leitura errada as três escritas responderiam 500.
    [Fact]
    public async Task DocumentWrites_WithTokenFromLogin_RecordOperatorAsAuthor()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest(ApiFactoryFixture.KnownOperatorUsername, ApiFactoryFixture.KnownOperatorPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var knowledgeBase = await client.CreateBaseAsync("Base Histórico Autor");
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, "Original");
        var update = await client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Renomeado", "markdown", KnowledgeTestClient.SampleMarkdown));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var delete = await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var events = await client.GetAllDocumentEventsAsync(knowledgeBase.Id);
        Assert.Equal(3, events.Count);
        Assert.All(events, documentEvent => Assert.Equal("operator", documentEvent.Author));
    }

    // --- Rota de eventos -----------------------------------------------------

    [Fact]
    public async Task DocumentEvents_ReturnOnlyEventsOfTheRequestedBase()
    {
        var baseA = await _client.CreateBaseAsync("Base Histórico A");
        var baseB = await _client.CreateBaseAsync("Base Histórico B");
        var documentA1 = await _client.CreateDocumentAsync(baseA.Id, "A1");
        var documentA2 = await _client.CreateDocumentAsync(baseA.Id, "A2");
        var documentB = await _client.CreateDocumentAsync(baseB.Id, "B");

        var eventsOfA = await _client.GetAllDocumentEventsAsync(baseA.Id);

        Assert.Equal(
            new[] { documentA1.Id, documentA2.Id }.Order(),
            eventsOfA.Select(documentEvent => documentEvent.DocumentId).Order());
        Assert.DoesNotContain(eventsOfA, documentEvent => documentEvent.DocumentId == documentB.Id);
    }

    // O cursor não carrega a base (D8): usado em outra base, ele só desloca a
    // posição dentro DELA.
    [Fact]
    public async Task DocumentEvents_CursorFromAnotherBase_ReturnsOnlyEventsOfTheRequestedBase()
    {
        var baseA = await _client.CreateBaseAsync("Base Cursor A");
        var baseB = await _client.CreateBaseAsync("Base Cursor B");
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 51; i++)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, baseA.Id, Guid.NewGuid(), t0.AddSeconds(i));
        }

        // Eventos de B mais antigos que o cursor de A, para que o deslocamento
        // devolva alguma coisa — e só de B.
        await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, baseB.Id, Guid.NewGuid(), t0.AddDays(-1));
        await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, baseB.Id, Guid.NewGuid(), t0.AddDays(-2));

        var cursorOfA = (await _client.GetDocumentEventsPageAsync(baseA.Id)).NextCursor;
        Assert.NotNull(cursorOfA);

        var page = await _client.GetDocumentEventsPageAsync(baseB.Id, cursorOfA);

        var idsOfB = (await _client.GetAllDocumentEventsAsync(baseB.Id)).Select(documentEvent => documentEvent.Id).ToHashSet();
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, documentEvent => Assert.Contains(documentEvent.Id, idsOfB));
    }

    [Fact]
    public async Task DocumentEvents_AreMostRecentFirst()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Ordem");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");
        var update = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Documento", "markdown", "# Outro\n\nTexto novo.\n"));
        update.EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}")).EnsureSuccessStatusCode();

        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);

        Assert.Equal(
            new[] { KnowledgeDocumentEventType.Deleted, KnowledgeDocumentEventType.Updated, KnowledgeDocumentEventType.Created },
            events.Select(documentEvent => documentEvent.Type));
    }

    // api-response-ordering na forma que vale: empatados inseridos em ordem
    // OPOSTA à esperada, e o grupo empatado atravessando a fronteira de página,
    // que é onde um cursor sem o Id erraria (repetindo ou pulando item).
    //
    // A ordem esperada de uuid é a do Postgres, que compara os 16 bytes na ordem
    // canônica — a mesma da string minúscula, e NÃO a de Guid.CompareTo do .NET.
    [Fact]
    public async Task DocumentEvents_TieOnOccurredAt_IsBrokenByIdDescending_AcrossThePageBoundary()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Empate");
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var expected = new List<Guid>();

        // 45 mais recentes, distintos: posições 1 a 45.
        var newest = Enumerable.Range(0, 45).Select(i => (Id: Guid.NewGuid(), At: t0.AddMinutes(100 - i))).ToList();
        // 6 empatados em t0: posições 46 a 51, atravessando a 50.
        var tied = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid())
            .OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        // 2 mais antigos: posições 52 e 53.
        var oldest = Enumerable.Range(0, 2).Select(i => (Id: Guid.NewGuid(), At: t0.AddMinutes(-1 - i))).ToList();

        foreach (var (id, at) in newest)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, id, at);
        }

        // Inseridos em ordem CRESCENTE de id; a rota tem de devolvê-los decrescentes.
        foreach (var id in tied)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, id, t0);
        }

        foreach (var (id, at) in oldest)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, id, at);
        }

        expected.AddRange(newest.Select(item => item.Id));
        expected.AddRange(Enumerable.Reverse(tied));
        expected.AddRange(oldest.Select(item => item.Id));

        var firstPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id);
        var secondPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id, firstPage.NextCursor);

        Assert.Equal(50, firstPage.Items.Count);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(expected, firstPage.Items.Concat(secondPage.Items).Select(documentEvent => documentEvent.Id));
    }

    [Fact]
    public async Task DocumentEvents_NewEventBetweenPages_IsNeitherRepeatedNorShifted()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Concorrente");
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 51; i++)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, Guid.NewGuid(), t0.AddSeconds(i));
        }

        var firstPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id);
        var newEventId = Guid.NewGuid();
        await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, newEventId, t0.AddDays(1));
        var secondPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id, firstPage.NextCursor);

        var firstIds = firstPage.Items.Select(documentEvent => documentEvent.Id).ToHashSet();
        var onlyOldest = Assert.Single(secondPage.Items);
        Assert.DoesNotContain(onlyOldest.Id, firstIds);
        Assert.NotEqual(newEventId, onlyOldest.Id);
        Assert.Equal(t0, onlyOldest.OccurredAt);
    }

    [Fact]
    public async Task DocumentEvents_WithExactlyOnePageOfEvents_HaveNullCursor()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico 50");
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 50; i++)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, Guid.NewGuid(), t0.AddSeconds(i));
        }

        var page = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id);

        Assert.Equal(50, page.Items.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task DocumentEvents_WithOneMoreThanAPage_HaveASecondPageOfOne()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico 51");
        var t0 = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 51; i++)
        {
            await KnowledgeTestClient.InsertCreatedEventAsync(factory.Services, knowledgeBase.Id, Guid.NewGuid(), t0.AddSeconds(i));
        }

        var firstPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id);
        var secondPage = await _client.GetDocumentEventsPageAsync(knowledgeBase.Id, firstPage.NextCursor);

        Assert.Equal(50, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        var oldest = Assert.Single(secondPage.Items);
        Assert.Equal(t0, oldest.OccurredAt);
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task DocumentEvents_ForBaseWithoutEvents_ReturnEmptyPage()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Vazia");

        var response = await _client.GetAsync(KnowledgeTestClient.DocumentEventsPath(knowledgeBase.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<KnowledgeDocumentEventPageResponse>();
        Assert.Empty(page!.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task DocumentEvents_ForMissingBase_ReturnNotFound()
    {
        var response = await _client.GetAsync(KnowledgeTestClient.DocumentEventsPath(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DocumentEvents_OfInactiveBase_AreReadable()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Inativa");
        await _client.CreateDocumentAsync(knowledgeBase.Id);
        (await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", content: null)).EnsureSuccessStatusCode();

        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);

        Assert.Single(events);
    }

    // Um caso por ramo de recusa de KnowledgeDocumentEventCursor.TryDecode. Qual
    // ramo cada valor atinge é conferido contra a BCL em
    // KnowledgeDocumentEventCursorTests, e não só pelo comentário.
    [Theory]
    [InlineData("abc$")]                             // captura: caractere fora do alfabeto base64url
    [InlineData("abcde")]                            // captura: resto 1 por 4, comprimento inválido
    [InlineData("nao-e-um-cursor")]                  // captura: bits finais de "r" não canônicos
    [InlineData("nao-e-um-cursoo")]                  // tamanho: decodifica para 11 bytes
    [InlineData("AAAA")]                             // tamanho: decodifica para 3 bytes
    [InlineData("")]                                 // tamanho: query vazia, 0 bytes
    [InlineData("__________8RERERIiIzM0REVVVVVVVV")] // faixa: 24 bytes, ticks = -1
    [InlineData("K8oodfQ3QAARERERIiIzM0REVVVVVVVV")] // faixa: 24 bytes, ticks = MaxValue.UtcTicks + 1
    public async Task DocumentEvents_WithMalformedCursor_ReturnValidationProblemOnCursor(string cursor)
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Cursor Inválido");

        var response = await _client.GetAsync(KnowledgeTestClient.DocumentEventsPath(knowledgeBase.Id, cursor));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("cursor", out _));
    }

    // Formato de fio afirmado sobre o TEXTO da resposta HTTP real, nunca por
    // desserialização para o mesmo tipo (convenção 11/12).
    [Fact]
    public async Task DocumentEvents_WireFormat_HasStringTypeAndCamelCaseKeys()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base Histórico Fio");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento");
        (await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Renomeado", "markdown", KnowledgeTestClient.SampleMarkdown))).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}")).EnsureSuccessStatusCode();

        var text = await _client.GetStringAsync(KnowledgeTestClient.DocumentEventsPath(knowledgeBase.Id));

        Assert.Contains("\"type\":\"Created\"", text);
        Assert.Contains("\"type\":\"Updated\"", text);
        Assert.Contains("\"type\":\"Deleted\"", text);
        foreach (var key in new[] { "items", "nextCursor", "id", "documentId", "documentTitle", "contentChanged", "titleChanged", "author", "occurredAt" })
        {
            Assert.Contains($"\"{key}\":", text);
        }
    }
}
