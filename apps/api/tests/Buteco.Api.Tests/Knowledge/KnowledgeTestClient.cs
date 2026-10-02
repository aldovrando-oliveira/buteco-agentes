using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Atalhos de arranjo compartilhados pelas classes de teste de conhecimento.
/// Só monta pré-condição — nenhuma asserção mora aqui.
/// </summary>
internal static class KnowledgeTestClient
{
    public const string SampleMarkdown = "# Política de trocas\n\nTrocas em até 30 dias.\n";

    public static async Task<KnowledgeBaseResponse> CreateBaseAsync(
        this HttpClient client, string name = "Base", string description = "Descrição da base.")
    {
        var response = await client.PostAsJsonAsync("/knowledge-bases", new CreateKnowledgeBaseRequest(name, description));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!;
    }

    public static async Task<KnowledgeDocumentResponse> CreateDocumentAsync(
        this HttpClient client, Guid knowledgeBaseId, string title = "Documento", string? content = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBaseId}/documents",
            new CreateKnowledgeDocumentRequest(title, "markdown", content ?? SampleMarkdown));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
    }

    public static async Task<KnowledgeDocumentResponse> ReindexDocumentAsync(
        this HttpClient client, Guid knowledgeBaseId, Guid documentId)
    {
        var response = await client.PostAsync(
            $"/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/reindex", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
    }

    /// <summary>
    /// Leva um documento ao estado terminal de falha <b>direto no banco</b>, com
    /// motivo, contagem de tentativas esgotada e carimbo da última.
    ///
    /// <para>
    /// Direto no banco porque <c>apps/api</c> <b>nunca escreve</b>
    /// <c>Failed</c> — quem escreve é o consumidor de <c>apps/workers</c>, que
    /// esta change não toca. É o mesmo motivo pelo qual
    /// <c>KnowledgeDocumentIndexingContractTests</c> fabrica fragmento por SQL.
    /// </para>
    ///
    /// <para>
    /// Grava também <c>IndexedAt</c> e <c>FragmentCount</c> quando
    /// <paramref name="withPreviousIndexing"/> — é o caso que importa para a
    /// garantia de que a reindexação <b>preserva</b> o conteúdo que já respondia.
    /// </para>
    /// </summary>
    public static async Task ForceFailedAsync(
        IServiceProvider services,
        Guid documentId,
        string failureReason = "O provedor de embedding respondeu 429 (limite de taxa).",
        int attempts = 3,
        bool withPreviousIndexing = false)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastAttemptAt = new DateTimeOffset(2026, 9, 1, 3, 14, 0, TimeSpan.Zero);
        var indexedAt = withPreviousIndexing ? new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null;

        var affected = await dbContext.KnowledgeDocuments
            .Where(document => document.Id == documentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(document => document.IndexingStatus, KnowledgeIndexingStatus.Failed)
                .SetProperty(document => document.FailureReason, failureReason)
                .SetProperty(document => document.IndexingAttempts, attempts)
                .SetProperty(document => document.LastAttemptAt, lastAttemptAt)
                .SetProperty(document => document.IndexedAt, indexedAt)
                .SetProperty(document => document.FragmentCount, withPreviousIndexing ? 12 : 0));

        Assert.Equal(1, affected);
    }

    public static string DocumentEventsPath(Guid knowledgeBaseId, string? cursor = null) =>
        cursor is null
            ? $"/knowledge-bases/{knowledgeBaseId}/document-events"
            : $"/knowledge-bases/{knowledgeBaseId}/document-events?cursor={Uri.EscapeDataString(cursor)}";

    public static async Task<KnowledgeDocumentEventPageResponse> GetDocumentEventsPageAsync(
        this HttpClient client, Guid knowledgeBaseId, string? cursor = null)
    {
        var response = await client.GetAsync(DocumentEventsPath(knowledgeBaseId, cursor));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentEventPageResponse>())!;
    }

    /// <summary>
    /// Segue o <c>nextCursor</c> até o fim e devolve todos os eventos da base, na
    /// ordem da rota. O limite de páginas existe só para um defeito de cursor
    /// (página que aponta para si mesma) reprovar o teste em vez de prendê-lo.
    /// </summary>
    public static async Task<List<KnowledgeDocumentEventResponse>> GetAllDocumentEventsAsync(
        this HttpClient client, Guid knowledgeBaseId)
    {
        var all = new List<KnowledgeDocumentEventResponse>();
        string? cursor = null;

        for (var page = 0; page < 100; page++)
        {
            var current = await client.GetDocumentEventsPageAsync(knowledgeBaseId, cursor);
            all.AddRange(current.Items);
            cursor = current.NextCursor;
            if (cursor is null)
            {
                return all;
            }
        }

        throw new InvalidOperationException("A rota de eventos não terminou em 100 páginas: cursor sem fim.");
    }

    /// <summary>
    /// Conta os eventos de uma base direto no banco. Para os cenários em que a
    /// rota não serve de testemunha — base inexistente responde 404, e o que se
    /// quer afirmar é que nenhuma linha nasceu com aquele <c>KnowledgeBaseId</c>.
    /// </summary>
    public static async Task<int> CountDocumentEventsInDatabaseAsync(IServiceProvider services, Guid knowledgeBaseId)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeDocumentEvents.CountAsync(
            documentEvent => documentEvent.KnowledgeBaseId == knowledgeBaseId);
    }

    /// <summary>
    /// Grava um evento <c>Created</c> direto no banco, com id e instante
    /// escolhidos pelo teste. É o único jeito de arranjar empate de
    /// <c>OccurredAt</c> e volume de página sem 50 cadastros pela rota —
    /// a escrita real é exercida nos testes de cada handler, não aqui.
    /// <paramref name="occurredAt"/> deve ter resolução de microssegundo, como o
    /// <c>timestamptz</c>.
    /// </summary>
    public static async Task InsertCreatedEventAsync(
        IServiceProvider services, Guid knowledgeBaseId, Guid id, DateTimeOffset occurredAt, string title = "Arranjo")
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO knowledge_document_events
                ("Id", "KnowledgeBaseId", "DocumentId", "DocumentTitle", "Type", "ContentChanged", "TitleChanged", "Author", "OccurredAt")
            VALUES ({id}, {knowledgeBaseId}, {Guid.NewGuid()}, {title}, 'Created', NULL, NULL, 'operator', {occurredAt});
            """);
    }

    /// <summary>
    /// Leva um documento a <c>Indexed</c> direto no banco, pelo mesmo motivo de
    /// <see cref="ForceFailedAsync"/>.
    /// </summary>
    public static async Task ForceIndexedAsync(IServiceProvider services, Guid documentId, int fragmentCount = 7)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await dbContext.KnowledgeDocuments
            .Where(document => document.Id == documentId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(document => document.IndexingStatus, KnowledgeIndexingStatus.Indexed)
                .SetProperty(document => document.IndexedAt, new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero))
                .SetProperty(document => document.FragmentCount, fragmentCount)
                .SetProperty(document => document.FailureReason, (string?)null)
                .SetProperty(document => document.IndexingAttempts, 1));

        Assert.Equal(1, affected);
    }
}
