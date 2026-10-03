using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// O Google falso (design.md, D11): responde por regra, gravando cada requisição. A
/// regra registrada por último vence, para um teste sobrescrever o padrão. Requisição
/// sem regra responde 599, que nenhum caminho do conector trata como sucesso.
/// </summary>
public sealed class GoogleDriveFakeHandler : HttpMessageHandler
{
    public const string TokenUrl = "https://oauth2.googleapis.com/token";

    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _rules = [];
    private readonly object _gate = new();
    private int _tokenCounter;

    public GoogleDriveFakeHandler()
    {
        // Troca de token padrão: um access_token diferente a cada troca, para o teste
        // de vazamento procurar o valor exato e o de cache contar as trocas.
        On(request => request.RequestUri!.AbsoluteUri == TokenUrl, _ =>
        {
            var n = Interlocked.Increment(ref _tokenCounter);
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                access_token = $"ya29.token-falso-{n}-{Guid.NewGuid():N}",
                expires_in = 3599,
                token_type = "Bearer",
            }));
        });
    }

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Atraso antes de responder, por caminho, respeitando o cancelamento.</summary>
    public ConcurrentDictionary<string, TimeSpan> Delays { get; } = new();

    public ConcurrentQueue<string> IssuedAccessTokens { get; } = new();

    public int TokenExchanges => Requests.Count(r => r.Uri.AbsoluteUri == TokenUrl);

    public IEnumerable<RecordedRequest> DriveRequests => Requests.Where(r => r.Uri.Host == "www.googleapis.com");

    public void On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            _rules.Add((match, respond));
        }
    }

    /// <summary>Regra por caminho exato da Drive API (sem a query).</summary>
    public void OnPath(string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        On(request => request.RequestUri!.Host == "www.googleapis.com" && request.RequestUri.AbsolutePath == path, respond);

    /// <summary>Regra para <c>files.list</c> cuja <c>q</c> contém o trecho.</summary>
    public void OnList(string queryFragment, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        On(request => request.RequestUri!.AbsolutePath == "/drive/v3/files" &&
            (Query(request, "q") ?? "").Contains(queryFragment, StringComparison.Ordinal), respond);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));

        if (Delays.TryGetValue(request.RequestUri!.AbsolutePath, out var delay))
        {
            await Task.Delay(delay, cancellationToken);
        }

        Func<HttpRequestMessage, HttpResponseMessage>? respond;
        lock (_gate)
        {
            respond = _rules.LastOrDefault(rule => rule.Match(request)).Respond;
        }

        var response = respond?.Invoke(request)
            ?? new HttpResponseMessage((HttpStatusCode)599) { Content = new StringContent("sem regra no fake") };

        if (request.RequestUri!.AbsoluteUri == TokenUrl && response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            IssuedAccessTokens.Enqueue(JsonDocument.Parse(json).RootElement.GetProperty("access_token").GetString()!);
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return response;
    }

    public static string? Query(HttpRequestMessage request, string name) =>
        HttpUtility.ParseQueryString(request.RequestUri!.Query)[name];

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Text(string text, string mediaType = "text/markdown") =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, mediaType) };

    /// <summary>
    /// Corpo de erro no formato documentado em
    /// developers.google.com/workspace/drive/api/guides/handle-errors.
    /// </summary>
    public static HttpResponseMessage GoogleError(HttpStatusCode status, string reason, string message, string domain = "global") =>
        Json(status, JsonSerializer.Serialize(new
        {
            error = new
            {
                code = (int)status,
                errors = new[] { new { domain, reason, message } },
                message,
            },
        }));

    public static HttpResponseMessage Fixture(string relativePath, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GoogleDrive", relativePath));
        return relativePath.EndsWith(".json", StringComparison.Ordinal) ? Json(status, text) : Text(text);
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization)
{
    public string? Query(string name) => HttpUtility.ParseQueryString(Uri.Query)[name];
}
