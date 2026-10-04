using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.KnowledgeSync.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PublisherMode = Buteco.Api.Tests.Support.FakeKnowledgeIndexingJobPublisher.PublisherMode;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Aceite da change indexacao-sem-job-orfao (#138): com a publicação na fila
/// falhando, a escrita responde o sucesso de sempre e o documento é indexado
/// depois, sem ação do operador, nos quatro caminhos de escrita (D1, D2) e no caso
/// permanente da base sincronizada (D4). <c>partial</c> da classe existente, pela
/// mesma fixture: nenhuma fonte de contêiner nova.
///
/// <para>
/// <b>Cada teste junta as duas observações do defeito numa asserção só</b> — a
/// resposta da escrita e as mensagens publicadas depois de a publicação voltar —,
/// para que a reprovação contra o código antigo diga as duas: <c>500</c> e nenhuma
/// mensagem depois. Só então vêm as asserções finas.
/// </para>
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    [Fact]
    public async Task Create_WithPublicationFailing_Returns201AndIsIndexedLater()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base fila fora cadastro");
        const string title = "Doc fila fora cadastro";

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await _client.PostAsJsonAsync(
                $"/knowledge-bases/{knowledgeBase.Id}/documents",
                new CreateKnowledgeDocumentRequest(title, "markdown", KnowledgeTestClient.SampleMarkdown));
        }

        var documentId = await SingleDocumentIdByTitleAsync(knowledgeBase.Id, title);
        await AssertIndexedAfterRecoveryAsync("cadastro", response, HttpStatusCode.Created, documentId, revision: 1, publishedBefore: 0);

        var body = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
        Assert.Equal(KnowledgeIndexingStatus.Pending, body.IndexingStatus);
    }

    [Fact]
    public async Task Update_WithPublicationFailing_Returns200AndIsIndexedLater()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base fila fora atualização");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc fila fora atualização");
        var publishedBefore = PublishedCount(document.Id, revision: 2);

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await _client.PutAsJsonAsync(
                $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
                new UpdateKnowledgeDocumentRequest("Doc fila fora atualização", "markdown", "# Conteúdo novo\n"));
        }

        await AssertIndexedAfterRecoveryAsync("atualização", response, HttpStatusCode.OK, document.Id, revision: 2, publishedBefore);
    }

    [Fact]
    public async Task Reindex_WithPublicationFailing_Returns200AndIsIndexedLater()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base fila fora reindexação");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc fila fora reindexação");
        var publishedBefore = PublishedCount(document.Id, revision: 1);

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await _client.PostAsync(
                $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}/reindex", content: null);
        }

        await AssertIndexedAfterRecoveryAsync("reindexação", response, HttpStatusCode.OK, document.Id, revision: 1, publishedBefore);
    }

    [Fact]
    public async Task UpsertNew_WithPublicationFailing_Returns200CreatedAndIsIndexedLater()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await Connectors.UpsertAsync(synced.Id, "ref-fila-fora-novo", "v1");
        }

        var documentId = (await FindByRefAsync(synced.Id, "ref-fila-fora-novo"))!.Id;
        await AssertIndexedAfterRecoveryAsync("upsert novo", response, HttpStatusCode.OK, documentId, revision: 1, publishedBefore: 0);

        var body = (await response.Content.ReadFromJsonAsync<UpsertSyncedDocumentResponse>())!;
        Assert.Equal(SyncedDocumentUpsertOutcome.Created, body.Outcome);
        Assert.Equal(1, await CountByRefAsync(synced.Id, "ref-fila-fora-novo"));
    }

    [Fact]
    public async Task UpsertExisting_WithPublicationFailing_Returns200UpdatedAndIsIndexedLater()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var documentId = (await Connectors.UpsertOkAsync(synced.Id, "ref-fila-fora-existente", "v1")).DocumentId;
        var publishedBefore = PublishedCount(documentId, revision: 2);

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await Connectors.UpsertAsync(synced.Id, "ref-fila-fora-existente", "v2", content: "# Texto novo\n");
        }

        await AssertIndexedAfterRecoveryAsync("upsert existente", response, HttpStatusCode.OK, documentId, revision: 2, publishedBefore);

        var body = (await response.Content.ReadFromJsonAsync<UpsertSyncedDocumentResponse>())!;
        Assert.Equal(SyncedDocumentUpsertOutcome.Updated, body.Outcome);
    }

    /// <summary>
    /// O caso permanente da #138 (comentário da #105): o upsert grava o texto novo e o
    /// <c>externalVersion</c> com a publicação falhando, e o ciclo seguinte não reenvia
    /// o arquivo. Aqui o reenvio do mesmo conteúdo com marcador novo responde
    /// <c>Unchanged</c>, que não pede indexação — e mesmo assim o documento recebe a
    /// mensagem da revisão nova, sem nenhum upsert a mais.
    /// </summary>
    [Fact]
    public async Task SyncedDocument_UpdatedWhilePublicationFails_IsIndexedEvenIfTheNextCycleDoesNotResend()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var documentId = (await Connectors.UpsertOkAsync(synced.Id, "ref-permanente", "v1")).DocumentId;
        var publishedBefore = PublishedCount(documentId, revision: 2);

        HttpResponseMessage failed;
        using (PublicationFailing())
        {
            failed = await Connectors.UpsertAsync(synced.Id, "ref-permanente", "v2", content: "# Texto da v2\n");
        }

        // O ciclo seguinte: mesmo conteúdo, marcador novo, publicação de volta. É o
        // Unchanged de D3 da catalogo-base-sincronizada, que não publica nada.
        var resent = await Connectors.UpsertOkAsync(synced.Id, "ref-permanente", "v3", content: "# Texto da v2\n");

        await AssertIndexedAfterRecoveryAsync("upsert sem reenvio", failed, HttpStatusCode.OK, documentId, revision: 2, publishedBefore);
        Assert.Equal(SyncedDocumentUpsertOutcome.Unchanged, resent.Outcome);
    }

    private const string RequestsTable = "knowledge_indexing_requests";

    /// <summary>
    /// Fica a publicação falhando até o <c>Dispose</c>. Quem cria o escopo o
    /// encerra antes de afirmar a recuperação.
    /// </summary>
    private IDisposable PublicationFailing()
    {
        factory.IndexingPublisher.Mode = PublisherMode.Unavailable;
        return new RestorePublication(factory);
    }

    /// <summary>
    /// Devolve a publicação e fecha a janela de D8, que é singleton da fixture: sem
    /// fechar, os testes seguintes da classe pulariam o despacho no fim da escrita.
    /// </summary>
    private sealed class RestorePublication(ApiFactoryFixture factory) : IDisposable
    {
        public void Dispose()
        {
            factory.IndexingPublisher.Mode = PublisherMode.Available;
            factory.Services.GetRequiredService<KnowledgeIndexingDispatchWindow>().Close();
        }
    }

    private int PublishedCount(Guid documentId, int revision) =>
        factory.IndexingPublisher.PublishedFor(documentId).Count(message => message.ContentRevision == revision);

    /// <summary>
    /// Um tique da varredura, chamado direto: a fixture a agenda para uma hora, e
    /// nenhum tique automático entra no meio de um teste. Na reprodução contra o
    /// código antigo (tarefa 1.4) isto era um <c>Task.CompletedTask</c>: não havia
    /// mecanismo nenhum que publicasse depois da escrita.
    /// </summary>
    private Task<int> RunSweepAsync() =>
        factory.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None);

    private async Task AssertIndexedAfterRecoveryAsync(
        string path, HttpResponseMessage response, HttpStatusCode expected, Guid documentId, int revision, int publishedBefore)
    {
        var publishedDuringFailure = PublishedCount(documentId, revision) - publishedBefore;

        await RunSweepAsync();
        var publishedAfterRecovery = PublishedCount(documentId, revision) - publishedBefore;

        Assert.True(
            response.StatusCode == expected && publishedAfterRecovery == 1,
            $"{path}: resposta {(int)response.StatusCode} (esperado {(int)expected}); " +
            $"mensagens da revisão {revision} publicadas depois de a publicação voltar: {publishedAfterRecovery} (esperado 1)");

        // Nada saiu enquanto a publicação falhava: a mensagem é a do despacho
        // posterior, não uma que tenha escapado do duplo.
        Assert.Equal(0, publishedDuringFailure);
        Assert.Equal(0, await CountRequestsAsync(documentId));
    }

    private async Task<long> CountRequestsAsync(Guid documentId)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringWithPassword());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select count(*) from {RequestsTable} where \"KnowledgeDocumentId\" = @id", connection);
        command.Parameters.AddWithValue("id", documentId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<Guid> SingleDocumentIdByTitleAsync(Guid knowledgeBaseId, string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await dbContext.KnowledgeDocuments
            .Where(document => document.KnowledgeBaseId == knowledgeBaseId && document.Title == title)
            .Select(document => document.Id)
            .ToListAsync();
        return Assert.Single(ids);
    }
}
