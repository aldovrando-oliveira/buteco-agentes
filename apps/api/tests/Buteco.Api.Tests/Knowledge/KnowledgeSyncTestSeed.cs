using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Auth;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeSync.Requests;
using Buteco.Api.KnowledgeSync.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Arranjo das bases sincronizadas (design.md da change catalogo-base-sincronizada,
/// D11 e D12). Nenhuma rota cria base <c>Synced</c> nesta change, então ela nasce
/// pelo <c>DbContext</c>, com a mesma fábrica que a #104 vai usar. Só monta
/// pré-condição — nenhuma asserção mora aqui.
/// </summary>
internal static class KnowledgeSyncTestSeed
{
    public const string Provider = "google-drive";

    public static async Task<KnowledgeBase> SeedSyncedBaseAsync(
        IServiceProvider services,
        string name = "Base sincronizada",
        string? folderId = null,
        string folderName = "Atendimento",
        bool isActive = true)
    {
        folderId ??= $"pasta-{Guid.NewGuid():N}";
        var knowledgeBase = KnowledgeBase.CreateSynced(
            name, "Descrição da base sincronizada.", Provider, folderId, folderName,
            $"https://drive.google.com/drive/folders/{folderId}");

        if (!isActive)
        {
            knowledgeBase.Deactivate();
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.KnowledgeBases.Add(knowledgeBase);
        await dbContext.SaveChangesAsync();
        return knowledgeBase;
    }

    public static async Task<KnowledgeBase> ReadBaseAsync(IServiceProvider services, Guid id)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeBases.AsNoTracking().SingleAsync(knowledgeBase => knowledgeBase.Id == id);
    }

    /// <summary>Cliente com token assinado para <paramref name="subject"/>, sem login.</summary>
    public static HttpClient CreateClientAs(WebApplicationFactory<Program> factory, string subject)
    {
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(subject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient CreateConnectorsClient(WebApplicationFactory<Program> factory) =>
        CreateClientAs(factory, ServiceScopeAuthorizationHandler.ConnectorsSubject);

    public static string DocumentsPath(Guid knowledgeBaseId) => $"/sync/knowledge-bases/{knowledgeBaseId}/documents";

    public static string SyncResultsPath(Guid knowledgeBaseId) => $"/sync/knowledge-bases/{knowledgeBaseId}/sync-results";

    public static Task<HttpResponseMessage> UpsertAsync(
        this HttpClient client,
        Guid knowledgeBaseId,
        string externalRef,
        string externalVersion = "v1",
        string title = "Documento sincronizado",
        string content = KnowledgeTestClient.SampleMarkdown) =>
        client.PutAsJsonAsync(
            DocumentsPath(knowledgeBaseId),
            new UpsertSyncedDocumentRequest(externalRef, externalVersion, title, "markdown", content));

    public static async Task<UpsertSyncedDocumentResponse> UpsertOkAsync(
        this HttpClient client,
        Guid knowledgeBaseId,
        string externalRef,
        string externalVersion = "v1",
        string title = "Documento sincronizado",
        string content = KnowledgeTestClient.SampleMarkdown)
    {
        var response = await client.UpsertAsync(knowledgeBaseId, externalRef, externalVersion, title, content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UpsertSyncedDocumentResponse>())!;
    }

    public static Task<HttpResponseMessage> RecordSuccessAsync(
        this HttpClient client, Guid knowledgeBaseId, string folderName = "Atendimento", params SyncIgnoredFileRequest[] ignoredFiles) =>
        client.PostAsJsonAsync(
            SyncResultsPath(knowledgeBaseId),
            new RecordSyncResultRequest("Succeeded", folderName, $"https://drive.google.com/drive/folders/{folderName}", ignoredFiles, null));

    public static Task<HttpResponseMessage> RecordFailureAsync(
        this HttpClient client, Guid knowledgeBaseId, string code = "access-denied", string? detail = "leitor@projeto.iam.gserviceaccount.com") =>
        client.PostAsJsonAsync(
            SyncResultsPath(knowledgeBaseId),
            new RecordSyncResultRequest("Failed", null, null, null, new SyncErrorRequest(code, detail)));
}

/// <summary>
/// Conta as vezes que um evento de log foi emitido. Os handlers de escrita de
/// documento emitem um evento próprio quando recuperam uma corrida — a
/// <c>UniqueViolation</c> da inclusão (D10) e a <c>DbUpdateConcurrencyException</c>
/// da atualização (D10, token de concorrência). É a evidência de que o teste de
/// corrida viu a corrida: sem ela, um laço em que as duas requisições nunca se
/// intercalaram passaria igual.
/// </summary>
internal sealed class LogEventCounter(int eventId) : ILoggerProvider
{
    private readonly int _eventId = eventId;

    private int _count;

    public int Count => Volatile.Read(ref _count);

    public ILogger CreateLogger(string categoryName) => new CountingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CountingLogger(LogEventCounter owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == owner._eventId)
            {
                Interlocked.Increment(ref owner._count);
            }
        }
    }
}
