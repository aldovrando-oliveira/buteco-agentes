using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Buteco.Connectors.Sync;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// O <c>apps/api</c> falso dos testes do ciclo (design.md da change
/// ciclo-de-sincronizacao, D11): responde às cinco rotas de <c>/sync</c> com o estado
/// configurado pelo teste e grava cada requisição, inclusive o <c>Authorization</c>.
/// <see cref="Override"/> força qualquer resposta, ou lança para simular falha de rede.
/// A forma real das respostas é provada pela ida e volta, não por este duplo.
/// </summary>
public sealed class FakeSyncApiHandler : HttpMessageHandler
{
    private readonly object _gate = new();

    public List<SyncedKnowledgeBase> Bases { get; } = [];

    /// <summary>Referência → (marcador, id), por base, comparada como veio.</summary>
    public Dictionary<Guid, Dictionary<string, (string Version, Guid Id)>> Documents { get; } = [];

    public ConcurrentQueue<RecordedApiRequest> Requests { get; } = new();

    /// <summary>Devolve uma resposta para a requisição, ou nulo para o comportamento padrão.</summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage?>? Override { get; set; }

    public SyncedKnowledgeBase AddBase(string provider = FakeConnector.Key, bool isActive = true)
    {
        var knowledgeBase = new SyncedKnowledgeBase(
            Guid.NewGuid(), provider, $"pasta-{Guid.NewGuid():N}", "Pasta gravada", "https://falso.test/gravada", isActive);
        lock (_gate)
        {
            Bases.Add(knowledgeBase);
            Documents[knowledgeBase.Id] = new Dictionary<string, (string, Guid)>(StringComparer.Ordinal);
        }

        return knowledgeBase;
    }

    public void AddDocument(Guid knowledgeBaseId, string externalRef, string version)
    {
        lock (_gate)
        {
            Documents[knowledgeBaseId][externalRef] = (version, Guid.NewGuid());
        }
    }

    public IReadOnlyList<RecordedApiRequest> RequestsTo(string method, string pathFragment) =>
        Requests.Where(request => request.Method == method && request.PathAndQuery.Contains(pathFragment, StringComparison.Ordinal)).ToList();

    public IReadOnlyList<RecordedApiRequest> Upserts(Guid knowledgeBaseId) => RequestsTo("PUT", $"/sync/knowledge-bases/{knowledgeBaseId}/documents");

    public IReadOnlyList<RecordedApiRequest> Deletes(Guid knowledgeBaseId) => RequestsTo("DELETE", $"/sync/knowledge-bases/{knowledgeBaseId}/documents");

    public IReadOnlyList<JsonElement> Results(Guid knowledgeBaseId) =>
        RequestsTo("POST", $"/sync/knowledge-bases/{knowledgeBaseId}/sync-results")
            .Select(request => JsonDocument.Parse(request.Body!).RootElement.Clone())
            .ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedApiRequest(
            request.Method.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.ToString()));

        var forced = Override?.Invoke(request, body);
        if (forced is not null)
        {
            return forced;
        }

        var segments = request.RequestUri.AbsolutePath.Trim('/').Split('/');
        if (segments is ["sync", "knowledge-bases"] && request.Method == HttpMethod.Get)
        {
            lock (_gate)
            {
                return Json(HttpStatusCode.OK, Bases);
            }
        }

        if (segments.Length < 4 || !Guid.TryParse(segments[2], out var knowledgeBaseId))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        lock (_gate)
        {
            if (!Documents.TryGetValue(knowledgeBaseId, out var documents))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            switch (segments[3], request.Method.Method)
            {
                case ("documents", "GET"):
                    return Json(HttpStatusCode.OK, documents
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new SyncedDocumentRef(pair.Key, pair.Value.Version, pair.Value.Id)));

                case ("documents", "PUT"):
                {
                    var upsert = JsonSerializer.Deserialize<SyncedDocumentUpsert>(body!, JsonSerializerOptions.Web)!;
                    string outcome;
                    Guid id;
                    if (documents.TryGetValue(upsert.ExternalRef, out var existing))
                    {
                        outcome = existing.Version == upsert.ExternalVersion ? "Unchanged" : "Updated";
                        id = existing.Id;
                    }
                    else
                    {
                        outcome = "Created";
                        id = Guid.NewGuid();
                    }

                    documents[upsert.ExternalRef] = (upsert.ExternalVersion, id);
                    return Json(HttpStatusCode.OK, new { documentId = id, outcome });
                }

                case ("documents", "DELETE"):
                {
                    var externalRef = Uri.UnescapeDataString(request.RequestUri.Query.Split("externalRef=")[1]);
                    documents.Remove(externalRef);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                case ("sync-results", "POST"):
                    return Json(HttpStatusCode.OK, new { id = knowledgeBaseId });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object value, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), Encoding.UTF8, mediaType) };

    /// <summary>A recusa de conteúdo real da #120, na forma medida na tarefa 1.2.</summary>
    public static HttpResponseMessage ContentRefusal(string code, int? contentBytes = null)
    {
        var problem = new Dictionary<string, object?>
        {
            ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            ["title"] = "One or more validation errors occurred.",
            ["status"] = 400,
            ["errors"] = new Dictionary<string, string[]> { ["content"] = ["frase do apps/api"] },
            ["code"] = code,
        };
        if (contentBytes is not null)
        {
            problem["contentBytes"] = contentBytes;
            problem["maxContentBytes"] = 1048576;
        }

        return Json(HttpStatusCode.BadRequest, problem, "application/problem+json");
    }

    /// <summary>A recusa de forma real: <c>400</c> sem <c>code</c>.</summary>
    public static HttpResponseMessage ShapeRefusal() =>
        Json(HttpStatusCode.BadRequest, new Dictionary<string, object?>
        {
            ["title"] = "One or more validation errors occurred.",
            ["status"] = 400,
            ["errors"] = new Dictionary<string, string[]> { ["externalRef"] = ["obrigatória"] },
        }, "application/problem+json");

    public static HttpResponseMessage Contention()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        return response;
    }
}

public sealed record RecordedApiRequest(string Method, string PathAndQuery, string? Body, string? Authorization);
