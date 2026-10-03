using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Auth;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Options;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;
using Buteco.Api.KnowledgeSync.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Documentos de base sincronizada (design.md da change catalogo-base-sincronizada):
/// a recusa do operador, os invariantes no banco, o upsert, a exclusão por
/// referência, o histórico e as corridas. <c>partial</c> da classe existente, para
/// não subir outro contêiner (D12).
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    private HttpClient Connectors => KnowledgeSyncTestSeed.CreateConnectorsClient(factory);

    private async Task<KnowledgeDocument?> FindByRefAsync(Guid knowledgeBaseId, string externalRef)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeDocuments.AsNoTracking()
            .SingleOrDefaultAsync(document => document.KnowledgeBaseId == knowledgeBaseId && document.ExternalRef == externalRef);
    }

    private async Task<int> CountByRefAsync(Guid knowledgeBaseId, string externalRef)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeDocuments
            .CountAsync(document => document.KnowledgeBaseId == knowledgeBaseId && document.ExternalRef == externalRef);
    }

    /// <summary>
    /// SQL direto pela conexão do próprio <c>DbContext</c>: <c>GetConnectionString()</c>
    /// devolve a string sem a senha.
    /// </summary>
    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string InsertDocumentSql(Guid knowledgeBaseId, string mode, string? externalRef, string? externalVersion) => $"""
        INSERT INTO knowledge_documents
            ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus", "ContentRevision",
             "CreatedAt", "UpdatedAt", "KnowledgeBaseContentMode", "ExternalRef", "ExternalVersion")
        VALUES ('{Guid.NewGuid()}', '{knowledgeBaseId}', 'Direto', 'markdown', '# Texto', 'Pending', 1,
                now(), now(), '{mode}', {(externalRef is null ? "NULL" : $"'{externalRef}'")}, {(externalVersion is null ? "NULL" : $"'{externalVersion}'")});
        """;

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // --- Operador em base sincronizada (D7) ------------------------------------

    [Fact]
    public async Task Operator_CreatingDocumentInSyncedBase_IsConflictAndWritesNothing()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var publishedBefore = factory.IndexingPublisher.Published.Count;

        var response = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Doc do operador", "markdown", KnowledgeTestClient.SampleMarkdown));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var listed = await _client.GetFromJsonAsync<List<KnowledgeDocumentSummaryResponse>>($"/knowledge-bases/{knowledgeBase.Id}/documents");
        Assert.Empty(listed!);
        Assert.Equal(0, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
        Assert.Equal(publishedBefore, factory.IndexingPublisher.Published.Count);
    }

    [Fact]
    public async Task Operator_UpdatingDocumentInSyncedBase_IsConflictAndChangesNothing()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var upserted = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-op-update");
        var before = await FindByRefAsync(knowledgeBase.Id, "ref-op-update");
        var eventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id);

        var response = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{upserted.DocumentId}",
            new UpdateKnowledgeDocumentRequest("Outro título", "markdown", "# Outro texto\n"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await FindByRefAsync(knowledgeBase.Id, "ref-op-update");
        Assert.Equal(before!.Title, after!.Title);
        Assert.Equal(before.ExtractedText, after.ExtractedText);
        Assert.Equal(before.ContentRevision, after.ContentRevision);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(eventsBefore, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
    }

    [Fact]
    public async Task Operator_DeletingDocumentInSyncedBase_IsConflictAndTheDocumentSurvives()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var upserted = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-op-delete");

        var response = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{upserted.DocumentId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(await FindByRefAsync(knowledgeBase.Id, "ref-op-delete"));
    }

    [Fact]
    public async Task Operator_ReindexingDocumentInSyncedBase_IsAllowed()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var upserted = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-op-reindex");
        var publishedBefore = factory.IndexingPublisher.PublishedFor(upserted.DocumentId).Count;

        var reindexed = await _client.ReindexDocumentAsync(knowledgeBase.Id, upserted.DocumentId);

        Assert.Equal(KnowledgeIndexingStatus.Pending, reindexed.IndexingStatus);
        Assert.Equal(publishedBefore + 1, factory.IndexingPublisher.PublishedFor(upserted.DocumentId).Count);
    }

    [Fact]
    public async Task Operator_WritingInManualBase_WorksAsBefore()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base manual continua igual");

        var created = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Doc", "markdown", KnowledgeTestClient.SampleMarkdown));
        var document = (await created.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
        var updated = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
            new UpdateKnowledgeDocumentRequest("Doc 2", "markdown", "# Outro\n"));
        var deleted = await _client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Operator_SendingExternalRefInTheBody_IsIgnored()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base manual com ref no corpo");

        var response = await _client.PostAsJsonAsync($"/knowledge-bases/{knowledgeBase.Id}/documents", new
        {
            title = "Doc", sourceType = "markdown", content = KnowledgeTestClient.SampleMarkdown,
            externalRef = "ref-intrusa", externalVersion = "v1",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KnowledgeDocuments
            .AsNoTracking().SingleAsync(document => document.Id == created.Id);
        Assert.Null(stored.ExternalRef);
        Assert.Null(stored.ExternalVersion);
    }

    // --- Invariantes no banco (D4) ----------------------------------------------

    [Fact]
    public async Task Database_RefusesExternalRefInManualBase()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base manual invariante");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(InsertDocumentSql(knowledgeBase.Id, "Manual", "ref-1", "v1")));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_documents_external_ref", exception.ConstraintName);
    }

    [Fact]
    public async Task Database_RefusesMissingExternalRefInSyncedBase()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(InsertDocumentSql(knowledgeBase.Id, "Synced", null, null)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_documents_external_ref", exception.ConstraintName);
    }

    [Fact]
    public async Task Database_RefusesDocumentWhoseModeCopyDisagreesWithTheBase()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base manual cópia divergente");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(InsertDocumentSql(knowledgeBase.Id, "Synced", "ref-1", "v1")));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Database_RefusesDuplicatedExternalRefInTheSameBase()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await ExecuteSqlAsync(InsertDocumentSql(knowledgeBase.Id, "Synced", "ref-dup", "v1"));

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteSqlAsync(InsertDocumentSql(knowledgeBase.Id, "Synced", "ref-dup", "v2")));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("IX_knowledge_documents_KnowledgeBaseId_ExternalRef", exception.ConstraintName);
    }

    // --- Upsert (D3, D8) ----------------------------------------------------------

    [Fact]
    public async Task Upsert_NewReference_CreatesDocumentEventAndIndexing()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-novo", "2026-10-03T12:00:00Z");

        Assert.Equal(SyncedDocumentUpsertOutcome.Created, result.Outcome);
        var stored = await FindByRefAsync(knowledgeBase.Id, "ref-novo");
        Assert.Equal("2026-10-03T12:00:00Z", stored!.ExternalVersion);
        Assert.Equal(result.DocumentId, stored.Id);
        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);
        Assert.Equal(KnowledgeDocumentEventType.Created, Assert.Single(events).Type);
        Assert.Single(factory.IndexingPublisher.PublishedFor(result.DocumentId));
    }

    [Fact]
    public async Task Upsert_NewMarkerWithSameTitleAndText_HasNoEffectBesidesTheMarker()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var created = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-marcador", "v1");
        var before = await FindByRefAsync(knowledgeBase.Id, "ref-marcador");
        var eventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id);
        var publishedBefore = factory.IndexingPublisher.PublishedFor(created.DocumentId).Count;

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-marcador", "v2");

        Assert.Equal(SyncedDocumentUpsertOutcome.Unchanged, result.Outcome);
        var after = await FindByRefAsync(knowledgeBase.Id, "ref-marcador");
        Assert.Equal("v2", after!.ExternalVersion);
        Assert.Equal(before!.ContentRevision, after.ContentRevision);
        Assert.Equal(before.IndexingStatus, after.IndexingStatus);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(eventsBefore, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
        Assert.Equal(publishedBefore, factory.IndexingPublisher.PublishedFor(created.DocumentId).Count);
    }

    [Fact]
    public async Task Upsert_NewText_UpdatesReindexesAndRecordsContentChange()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var created = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-texto", "v1");

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-texto", "v2", content: "# Texto novo\n");

        Assert.Equal(SyncedDocumentUpsertOutcome.Updated, result.Outcome);
        var stored = await FindByRefAsync(knowledgeBase.Id, "ref-texto");
        Assert.Equal(2, stored!.ContentRevision);
        var updated = (await _client.GetAllDocumentEventsAsync(knowledgeBase.Id)).First();
        Assert.Equal(KnowledgeDocumentEventType.Updated, updated.Type);
        Assert.True(updated.ContentChanged);
        Assert.Equal(2, factory.IndexingPublisher.PublishedFor(created.DocumentId).Count);
    }

    [Fact]
    public async Task Upsert_OnlyNewTitle_RecordsTitleChangeWithoutIndexing()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var created = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-titulo", "v1", title: "Nome antigo.md");

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-titulo", "v2", title: "Nome novo.md");

        Assert.Equal(SyncedDocumentUpsertOutcome.Updated, result.Outcome);
        var updated = (await _client.GetAllDocumentEventsAsync(knowledgeBase.Id)).First();
        Assert.Equal(KnowledgeDocumentEventType.Updated, updated.Type);
        Assert.True(updated.TitleChanged);
        Assert.False(updated.ContentChanged);
        Assert.Single(factory.IndexingPublisher.PublishedFor(created.DocumentId));
    }

    [Fact]
    public async Task Upsert_SameContentWithBomAndCrlf_IsUnchangedAfterNormalization()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-bom", "v1", content: "﻿# Título\r\n\r\nTexto.\r\n");

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-bom", "v2", content: "# Título\n\nTexto.\n");

        Assert.Equal(SyncedDocumentUpsertOutcome.Unchanged, result.Outcome);
    }

    [Fact]
    public async Task Upsert_AboveTheSizeCap_IsRefusedAndKeepsTheMarker()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-grande", "v1");

        var response = await Connectors.UpsertAsync(
            knowledgeBase.Id, "ref-grande", "v2", content: new string('a', KnowledgeDocumentLimits.MaxContentBytes + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("v1", (await FindByRefAsync(knowledgeBase.Id, "ref-grande"))!.ExternalVersion);
    }

    [Fact]
    public async Task Upsert_ReferenceIsComparedAsItCame()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "AbC");

        var result = await Connectors.UpsertOkAsync(knowledgeBase.Id, "abc");

        Assert.Equal(SyncedDocumentUpsertOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task Upsert_InInactiveBase_IsAccepted()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, isActive: false);

        var response = await Connectors.UpsertAsync(knowledgeBase.Id, "ref-inativa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_InManualBase_IsConflict()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base manual upsert");

        var response = await Connectors.UpsertAsync(knowledgeBase.Id, "ref-manual");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Upsert_InMissingBase_IsNotFound()
    {
        var response = await Connectors.UpsertAsync(Guid.NewGuid(), "ref-sem-base");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Upsert_WithoutExternalRef_IsRefused(string? externalRef)
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.PutAsJsonAsync(KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id), new
        {
            externalRef, externalVersion = "v1", title = "Doc", sourceType = "markdown", content = "# Texto\n",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listed = await Connectors.GetFromJsonAsync<List<SyncedDocumentRefResponse>>(KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id));
        Assert.Empty(listed!);
    }

    // --- Corridas (D10) -------------------------------------------------------------

    /// <summary>
    /// Dois upserts idênticos de referência nova, ao mesmo tempo, repetidos em laço
    /// curto. O contador de log mede quantas iterações de fato passaram pelo
    /// <c>catch</c> de <c>UniqueViolation</c>: sem ele, um laço em que as duas
    /// requisições nunca se intercalaram passaria igual, e não provaria nada.
    /// </summary>
    [Fact]
    public async Task ConcurrentIdenticalUpserts_OfANewReference_EndWithOneDocument()
    {
        const int iterations = 30;
        var counter = new LogEventCounter(UpsertSyncedDocumentCommandHandler.ConcurrentUpsertRecoveredEvent.Id);
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(counter);
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var first = Connectors;
        var second = Connectors;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var externalRef = $"ref-corrida-{iteration}";

            var responses = await Task.WhenAll(
                Task.Run(() => first.UpsertAsync(knowledgeBase.Id, externalRef)),
                Task.Run(() => second.UpsertAsync(knowledgeBase.Id, externalRef)));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var outcomes = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<UpsertSyncedDocumentResponse>()));
            Assert.Equal(
                [SyncedDocumentUpsertOutcome.Created, SyncedDocumentUpsertOutcome.Unchanged],
                outcomes.Select(outcome => outcome!.Outcome).Order());
            Assert.Equal(1, await CountByRefAsync(knowledgeBase.Id, externalRef));
            var document = await FindByRefAsync(knowledgeBase.Id, externalRef);
            Assert.Single(factory.IndexingPublisher.PublishedFor(document!.Id));
        }

        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);
        Assert.Equal(iterations, events.Count(documentEvent => documentEvent.Type == KnowledgeDocumentEventType.Created));
        Assert.DoesNotContain(events, documentEvent => documentEvent.Type == KnowledgeDocumentEventType.Updated);

        // Evidência de que a corrida aconteceu. O número exato depende do
        // escalonamento; zero significaria que o laço nunca exercitou o catch.
        Assert.True(counter.Count > 0, $"Nenhuma das {iterations} iterações passou pelo catch de UniqueViolation.");
        Console.WriteLine($"[corrida upsert idêntico] {counter.Count} de {iterations} iterações passaram pelo catch de UniqueViolation.");
    }

    /// <summary>
    /// Dois upserts com textos diferentes sobre um documento que já existe, ao mesmo
    /// tempo (D10, token de concorrência). Os dois leem a revisão N; sem o token, os
    /// dois gravavam N+1 e as duas publicações de indexação carregavam a MESMA
    /// revisão para textos diferentes — e o indexador, que descarta pela revisão,
    /// podia gravar os fragmentos de um texto sobre o outro. Com o token, o perdedor
    /// relê e reaplica: as publicações carregam N+1 e N+2, e a revisão final é N+2.
    /// O contador de log mede quantas iterações passaram pela releitura.
    /// </summary>
    [Fact]
    public async Task ConcurrentUpserts_WithDifferentTexts_OnAnExistingDocument_GetDistinctRevisions()
    {
        const int iterations = 20;
        var counter = new LogEventCounter(UpsertSyncedDocumentCommandHandler.ConcurrentUpdateRetriedEvent.Id);
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(counter);
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var created = await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-existente");
        var first = Connectors;
        var second = Connectors;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var before = (await FindByRefAsync(knowledgeBase.Id, "ref-existente"))!.ContentRevision;
            var publishedBefore = factory.IndexingPublisher.PublishedFor(created.DocumentId).Count;
            var textA = $"# Versão A {iteration}\n";
            var textB = $"# Versão B {iteration}\n";

            var responses = await Task.WhenAll(
                Task.Run(() => first.UpsertAsync(knowledgeBase.Id, "ref-existente", $"a{iteration}", content: textA)),
                Task.Run(() => second.UpsertAsync(knowledgeBase.Id, "ref-existente", $"b{iteration}", content: textB)));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, await CountByRefAsync(knowledgeBase.Id, "ref-existente"));
            var stored = await FindByRefAsync(knowledgeBase.Id, "ref-existente");
            Assert.Contains(stored!.ExtractedText, new[] { textA, textB });
            Assert.Equal(Sha256(stored.ExtractedText), stored.ContentHash);

            // As duas escritas aparecem na revisão: N+2, e duas publicações com
            // revisões diferentes, N+1 e N+2.
            var published = factory.IndexingPublisher.PublishedFor(created.DocumentId)
                .Skip(publishedBefore)
                .Select(message => message.ContentRevision)
                .Order()
                .ToList();
            Assert.Equal([before + 1, before + 2], published);
            Assert.Equal(before + 2, stored.ContentRevision);
        }

        Console.WriteLine($"[corrida upsert sobre existente] {counter.Count} de {iterations} iterações passaram pela releitura de DbUpdateConcurrencyException.");
        Assert.True(counter.Count > 0, $"Nenhuma das {iterations} iterações passou pela releitura de concorrência.");
    }

    /// <summary>
    /// O mesmo cenário pelo caminho do operador: dois <c>PUT</c> simultâneos com
    /// textos diferentes sobre um documento de base manual. O defeito existia antes
    /// da #102 neste caminho. Aqui a resposta traz a revisão e o texto, então dá para
    /// afirmar que o texto gravado é o da escrita que ficou com N+2.
    /// </summary>
    [Fact]
    public async Task ConcurrentOperatorUpdates_WithDifferentTexts_GetDistinctRevisions()
    {
        const int iterations = 20;
        var counter = new LogEventCounter(
            Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument.UpdateKnowledgeDocumentCommandHandler.ConcurrentUpdateRetriedEvent.Id);
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(counter);
        var knowledgeBase = await _client.CreateBaseAsync("Base manual corrida de PUT");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc corrida");
        var first = factory.CreateClient();
        var second = factory.CreateClient();

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var before = (await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(
                $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}"))!.ContentRevision;
            var publishedBefore = factory.IndexingPublisher.PublishedFor(document.Id).Count;
            var path = $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}";

            var responses = await Task.WhenAll(
                Task.Run(() => first.PutAsJsonAsync(path, new UpdateKnowledgeDocumentRequest("Doc corrida", "markdown", $"# PUT A {iteration}\n"))),
                Task.Run(() => second.PutAsJsonAsync(path, new UpdateKnowledgeDocumentRequest("Doc corrida", "markdown", $"# PUT B {iteration}\n"))));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>()));
            var stored = (await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(path))!;

            Assert.Equal([before + 1, before + 2], bodies.Select(body => body!.ContentRevision).Order());
            Assert.Equal(before + 2, stored.ContentRevision);
            Assert.Equal(bodies.Single(body => body!.ContentRevision == before + 2)!.ExtractedText, stored.ExtractedText);
            var published = factory.IndexingPublisher.PublishedFor(document.Id)
                .Skip(publishedBefore)
                .Select(message => message.ContentRevision)
                .Order()
                .ToList();
            Assert.Equal([before + 1, before + 2], published);
        }

        Console.WriteLine($"[corrida PUT do operador] {counter.Count} de {iterations} iterações passaram pela releitura de DbUpdateConcurrencyException.");
        Assert.True(counter.Count > 0, $"Nenhuma das {iterations} iterações passou pela releitura de concorrência.");
    }

    // --- Exclusão e listagens (D8, D9) ---------------------------------------------

    [Fact]
    public async Task DeleteByReference_RemovesTheDocumentWithServiceAuthor()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-excluir");

        var response = await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id)}?externalRef=ref-excluir");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await FindByRefAsync(knowledgeBase.Id, "ref-excluir"));
        var deleted = (await _client.GetAllDocumentEventsAsync(knowledgeBase.Id)).First();
        Assert.Equal(KnowledgeDocumentEventType.Deleted, deleted.Type);
        Assert.Equal(ServiceScopeAuthorizationHandler.ConnectorsSubject, deleted.Author);
    }

    [Fact]
    public async Task DeleteByReference_Missing_IsNoContentWithoutEvent()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id)}?externalRef=ref-inexistente");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
    }

    [Fact]
    public async Task DeleteByReference_OnlyInTheRequestedBase()
    {
        var baseA = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base A");
        var baseB = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base B");
        await Connectors.UpsertOkAsync(baseA.Id, "ref-compartilhada");
        await Connectors.UpsertOkAsync(baseB.Id, "ref-compartilhada");

        (await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(baseA.Id)}?externalRef=ref-compartilhada")).EnsureSuccessStatusCode();

        Assert.Null(await FindByRefAsync(baseA.Id, "ref-compartilhada"));
        Assert.NotNull(await FindByRefAsync(baseB.Id, "ref-compartilhada"));
    }

    [Fact]
    public async Task DeleteByReference_InManualBase_IsConflict_AndWithoutReference_IsBadRequest()
    {
        var manual = await _client.CreateBaseAsync("Base manual exclusão por ref");
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var onManual = await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(manual.Id)}?externalRef=ref-1");
        var withoutRef = await Connectors.DeleteAsync(KnowledgeSyncTestSeed.DocumentsPath(synced.Id));
        var onMissing = await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(Guid.NewGuid())}?externalRef=ref-1");

        Assert.Equal(HttpStatusCode.Conflict, onManual.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutRef.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, onMissing.StatusCode);
    }

    [Fact]
    public async Task DocumentRefs_ListTheVersionsOfTheLastUpsert_AndRefuseManualBase()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var manual = await _client.CreateBaseAsync("Base manual refs");
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-b", "v1");
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-a", "v1");
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-a", "v2");

        var refs = await Connectors.GetFromJsonAsync<List<SyncedDocumentRefResponse>>(KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id));
        var onManual = await Connectors.GetAsync(KnowledgeSyncTestSeed.DocumentsPath(manual.Id));

        Assert.Equal(["ref-a", "ref-b"], refs!.Select(item => item.ExternalRef));
        Assert.Equal(["v2", "v1"], refs.Select(item => item.ExternalVersion));
        Assert.Equal(HttpStatusCode.Conflict, onManual.StatusCode);
    }

    [Fact]
    public async Task SyncedBases_ListIncludesInactiveAndExcludesManual()
    {
        var active = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Sincronizada ativa");
        var inactive = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Sincronizada inativa", isActive: false);
        var manual = await _client.CreateBaseAsync("Manual fora da lista");

        var listed = await Connectors.GetFromJsonAsync<List<SyncedKnowledgeBaseResponse>>("/sync/knowledge-bases");

        Assert.Contains(listed!, item => item.Id == active.Id && item.IsActive);
        Assert.Contains(listed!, item => item.Id == inactive.Id && !item.IsActive);
        Assert.DoesNotContain(listed!, item => item.Id == manual.Id);
    }

    // --- Histórico (#98) ---------------------------------------------------------

    [Fact]
    public async Task ServiceWrites_RecordServiceConnectorsAsAuthor()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-autor", "v1");
        await Connectors.UpsertOkAsync(knowledgeBase.Id, "ref-autor", "v2", content: "# Outro\n");
        (await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id)}?externalRef=ref-autor")).EnsureSuccessStatusCode();

        var events = await _client.GetAllDocumentEventsAsync(knowledgeBase.Id);
        Assert.Equal(3, events.Count);
        Assert.All(events, documentEvent => Assert.Equal("service:connectors", documentEvent.Author));
    }
}
