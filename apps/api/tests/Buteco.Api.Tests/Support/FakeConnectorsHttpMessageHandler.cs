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

    private int _inFlight;

    /// <summary>
    /// Requisições que entraram em <see cref="SendAsync"/> e ainda não saíram. Num host derivado compartilhado entre
    /// casos (#303, D4), é o estado que pode vazar de um caso para o seguinte.
    /// </summary>
    public int InFlight => Volatile.Read(ref _inFlight);

    /// <summary>O caso que pediu o host por último, pelo <see cref="Reset"/>. Nulo num handler que nunca foi reposto.</summary>
    public string? CaseName { get; private set; }

    /// <summary>
    /// Volta ao estado de um handler novo para o caso <paramref name="caseName"/>, num host derivado compartilhado
    /// entre os casos de uma classe (#303, D4, segunda linha). Requisição de outro caso ainda em andamento não é
    /// esperada nem descartada: reprova este caso, com o nome do anterior, porque esperar esconderia o vazamento.
    /// </summary>
    public void Reset(string caseName)
    {
        var inFlight = InFlight;
        if (inFlight > 0)
        {
            throw new InvalidOperationException(
                $"{inFlight} requisição(ões) ao connectors falso ainda em andamento, deixada(s) pelo caso {CaseName ?? "(desconhecido)"}, " +
                $"ao repor o handler para o caso {caseName}");
        }

        _requests.Clear();
        _barrier = null;
        _barrierParticipants = 0;
        _barrierArrived = 0;
        Respond = (request, _) => Task.FromResult(DescribeRequestedFolder(request));
        CaseName = caseName;
    }

    /// <summary>
    /// A primeira linha da D4 (#303): no fim do caso, nenhuma requisição dele pode continuar em andamento. Lança no
    /// próprio caso que vazou, com o nome dele.
    /// </summary>
    /// <remarks>
    /// Lançada no descarte, ela substitui uma reprovação por asserção que o caso já tivesse (#303, design, "O que o
    /// apply mudou"). A mensagem diz isso, e manda rodar o caso isolado para ver a asserção.
    /// </remarks>
    public void EnsureNothingInFlight()
    {
        var inFlight = InFlight;
        if (inFlight > 0)
        {
            var caseName = CaseName ?? "(desconhecido)";
            throw new InvalidOperationException(
                $"o caso {caseName} terminou com {inFlight} requisição(ões) ao connectors falso em andamento. " +
                $"Se o caso também reprovou por asserção, esta exceção do descarte a encobriu: rode o caso {caseName} isolado para vê-la.");
        }
    }

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
        Interlocked.Increment(ref _inFlight);
        try
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
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
}
