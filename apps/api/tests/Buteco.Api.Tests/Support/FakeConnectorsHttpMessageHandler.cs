using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O <c>apps/connectors</c> falso, como handler primário do <c>HttpClient</c> nomeado
/// do <c>apps/api</c> (design.md da change criacao-base-sincronizada, D9). Os corpos
/// são forjados aqui; a resposta REAL do <c>apps/connectors</c> é exercitada pela ida
/// e volta em <c>tests/ApiConnectorsRoundTrip.Tests</c> (convenção 11).
///
/// <para>
/// Por padrão descreve a pasta pedida com <see cref="FolderName"/> e uma URL derivada
/// do id. Registra cada chamada (URL e <c>Authorization</c>), e pode segurar as
/// chamadas numa barreira até <c>N</c> chegarem: é o que torna a corrida da D5
/// determinística.
/// </para>
/// </summary>
public sealed class FakeConnectorsHttpMessageHandler : HttpMessageHandler
{
    public const string FolderName = "Nome vindo do apps/connectors";

    private readonly ConcurrentQueue<(Uri Uri, AuthenticationHeaderValue? Authorization)> _requests = new();

    private TaskCompletionSource? _barrier;

    private int _barrierParticipants;

    private int _barrierArrived;

    public static string FolderUrl(string folderId) => $"https://conector.test/pastas/{Uri.EscapeDataString(folderId)}";

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (request, _) => Task.FromResult(DescribeRequestedFolder(request));

    public IReadOnlyList<(Uri Uri, AuthenticationHeaderValue? Authorization)> Requests => [.. _requests];

    public int RequestCount => _requests.Count;

    /// <summary>Segura as próximas <paramref name="participants"/> chamadas até todas chegarem.</summary>
    public void HoldUntil(int participants)
    {
        _barrierArrived = 0;
        _barrierParticipants = participants;
        _barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static HttpResponseMessage DescribeRequestedFolder(HttpRequestMessage request)
    {
        var folderId = HttpUtility.ParseQueryString(request.RequestUri!.Query)["id"]!;
        return Json(HttpStatusCode.OK, $$"""{"id":{{Quote(folderId)}},"name":{{Quote(FolderName)}},"webUrl":{{Quote(FolderUrl(folderId))}}}""");
    }

    /// <summary>Erro na forma do <c>apps/connectors</c>: <c>detail</c> omitido quando nulo.</summary>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string? detail = null)
    {
        var detailPart = detail is null ? "" : $",\"detail\":{Quote(detail)}";
        var response = Json(status, $"{{\"title\":\"Falha.\",\"status\":{(int)status}{detailPart},\"code\":{Quote(code)}}}");
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
        return response;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue((request.RequestUri!, request.Headers.Authorization));

        var barrier = _barrier;
        if (barrier is not null)
        {
            if (Interlocked.Increment(ref _barrierArrived) >= _barrierParticipants)
            {
                barrier.TrySetResult();
            }

            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }

        return await Respond(request, cancellationToken);
    }

    private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
