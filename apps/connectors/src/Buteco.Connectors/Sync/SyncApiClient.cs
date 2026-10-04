using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Connectors.Connectors;

namespace Buteco.Connectors.Sync;

public interface ISyncApiClient
{
    Task<SyncApiResponse<IReadOnlyList<SyncedKnowledgeBase>>> ListKnowledgeBasesAsync(CancellationToken cancellationToken);

    Task<SyncApiResponse<IReadOnlyList<SyncedDocumentRef>>> ListDocumentRefsAsync(Guid knowledgeBaseId, CancellationToken cancellationToken);

    Task<SyncApiResponse<string>> UpsertAsync(Guid knowledgeBaseId, SyncedDocumentUpsert document, CancellationToken cancellationToken);

    Task<SyncApiResponse<bool>> DeleteAsync(Guid knowledgeBaseId, string externalRef, CancellationToken cancellationToken);

    Task<SyncApiResponse<bool>> RecordResultAsync(Guid knowledgeBaseId, SyncCycleResult result, CancellationToken cancellationToken);
}

/// <summary>
/// As cinco chamadas de <c>/sync/knowledge-bases</c> no <c>apps/api</c> (D8 da #102), com o
/// token de <see cref="Auth.ServiceTokenDelegatingHandler"/> e limite fixo de 30 s por
/// chamada. Cada resposta vira um <see cref="SyncApiStatus"/> (design.md da change
/// ciclo-de-sincronizacao, D4, D7, D8); o <c>code</c> da recusa de conteúdo é lido do
/// texto da resposta, nunca da frase.
/// </summary>
public sealed class SyncApiClient(IHttpClientFactory httpClientFactory) : ISyncApiClient
{
    public const string HttpClientName = "apps-api";

    /// <summary>
    /// Fixo (convenção 2). O upsert leva até 1 MiB de texto e o <c>apps/api</c> só grava e
    /// publica; 30 s é o mesmo limite de metadado do Google no app.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private const string BasesPath = "/sync/knowledge-bases";

    public Task<SyncApiResponse<IReadOnlyList<SyncedKnowledgeBase>>> ListKnowledgeBasesAsync(CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, BasesPath),
            async content => (IReadOnlyList<SyncedKnowledgeBase>)(await content.ReadFromJsonAsync<List<SyncedKnowledgeBase>>(JsonSerializerOptions.Web, cancellationToken))!,
            cancellationToken);

    public Task<SyncApiResponse<IReadOnlyList<SyncedDocumentRef>>> ListDocumentRefsAsync(Guid knowledgeBaseId, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, DocumentsPath(knowledgeBaseId)),
            async content => (IReadOnlyList<SyncedDocumentRef>)(await content.ReadFromJsonAsync<List<SyncedDocumentRef>>(JsonSerializerOptions.Web, cancellationToken))!,
            cancellationToken);

    public Task<SyncApiResponse<string>> UpsertAsync(Guid knowledgeBaseId, SyncedDocumentUpsert document, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Put, DocumentsPath(knowledgeBaseId)) { Content = JsonContent.Create(document, options: JsonSerializerOptions.Web) },
            async content => (await JsonDocument.ParseAsync(await content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken))
                .RootElement.GetProperty("outcome").GetString()!,
            cancellationToken);

    public Task<SyncApiResponse<bool>> DeleteAsync(Guid knowledgeBaseId, string externalRef, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, $"{DocumentsPath(knowledgeBaseId)}?externalRef={Uri.EscapeDataString(externalRef)}"),
            _ => Task.FromResult(true),
            cancellationToken);

    public Task<SyncApiResponse<bool>> RecordResultAsync(Guid knowledgeBaseId, SyncCycleResult result, CancellationToken cancellationToken) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, $"{BasesPath}/{knowledgeBaseId}/sync-results") { Content = JsonContent.Create(result, options: JsonSerializerOptions.Web) },
            _ => Task.FromResult(true),
            cancellationToken);

    private static string DocumentsPath(Guid knowledgeBaseId) => $"{BasesPath}/{knowledgeBaseId}/documents";

    private async Task<SyncApiResponse<T>> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        Func<HttpContent, Task<T>> readValue,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using var request = createRequest();
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new SyncApiResponse<T>(SyncApiStatus.Unreachable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout: o token de quem chamou não foi cancelado.
            return new SyncApiResponse<T>(SyncApiStatus.Unreachable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return new SyncApiResponse<T>(SyncApiStatus.Ok, await readValue(response.Content));
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    return new SyncApiResponse<T>(SyncApiStatus.ContractError);
                }
            }

            return response.StatusCode switch
            {
                HttpStatusCode.BadRequest => await ReadContentRefusalAsync<T>(response.Content, cancellationToken),
                HttpStatusCode.NotFound or HttpStatusCode.Conflict => new SyncApiResponse<T>(SyncApiStatus.BaseGone),
                HttpStatusCode.ServiceUnavailable => new SyncApiResponse<T>(SyncApiStatus.Contention),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new SyncApiResponse<T>(SyncApiStatus.Rejected),
                _ => new SyncApiResponse<T>(SyncApiStatus.ContractError),
            };
        }
    }

    /// <summary>
    /// <c>400</c> com <c>code</c> no formato de código é recusa de conteúdo (#120); sem
    /// <c>code</c>, ou com uma frase no lugar, é recusa de forma, defeito do conector.
    /// O <c>contentBytes</c> do <c>too-large</c> vira o detalhe, como texto (D13).
    /// </summary>
    private static async Task<SyncApiResponse<T>> ReadContentRefusalAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("code", out var codeElement) ||
                codeElement.ValueKind != JsonValueKind.String ||
                !ConnectorCodes.IsValid(codeElement.GetString()))
            {
                return new SyncApiResponse<T>(SyncApiStatus.ContractError);
            }

            var detail = root.TryGetProperty("contentBytes", out var bytes) && bytes.ValueKind == JsonValueKind.Number
                ? bytes.GetRawText()
                : null;
            return new SyncApiResponse<T>(SyncApiStatus.ContentRefused, Code: codeElement.GetString(), Detail: detail);
        }
        catch (JsonException)
        {
            return new SyncApiResponse<T>(SyncApiStatus.ContractError);
        }
    }
}
