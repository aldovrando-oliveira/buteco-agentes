using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Buteco.Inbox.Tests.Support;

// Duplicado deliberadamente de
// apps/connectors/tests/Buteco.Connectors.Tests/Support/FakeTokenEndpoint.cs — projetos de
// teste separados, sem código compartilhado.

/// <summary>
/// Duplo do <c>POST /connect/token</c> do emissor (design.md da change servicos-token-emissor,
/// D9): conta os pedidos, guarda os formulários recebidos, segura a resposta até o teste
/// soltar, e responde o que o teste mandar. Sem nada programado, responde um token opaco
/// novo com <c>expires_in</c> 900.
/// </summary>
public sealed class FakeTokenEndpoint : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<Task<HttpResponseMessage>>> _scripted = new();
    private readonly SemaphoreSlim _arrived = new(0);
    private int _issued;

    public int Requests => Forms.Count;

    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Forms { get; } = new();

    public ConcurrentQueue<string> RawBodies { get; } = new();

    /// <summary>Os tokens que o duplo emitiu, na ordem.</summary>
    public ConcurrentQueue<string> Issued { get; } = new();

    /// <summary>O próximo pedido recebe um token com este <c>expires_in</c>.</summary>
    public void RespondToken(double expiresIn = 900) =>
        _scripted.Enqueue(() => Task.FromResult(TokenResponse(expiresIn)));

    public void RespondStatus(HttpStatusCode status, string body = "{}") =>
        _scripted.Enqueue(() => Task.FromResult(Json(status, body)));

    // 401, como o emissor real responde (medido no acordo de tests/, tarefa 5.4).
    public void RespondInvalidClient() =>
        RespondStatus(HttpStatusCode.Unauthorized, """{"error":"invalid_client","error_description":"The specified client credentials are invalid."}""");

    /// <summary>O próximo pedido fica sem resposta até o teste soltar (ou para sempre).</summary>
    public TaskCompletionSource Hold()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scripted.Enqueue(async () =>
        {
            await release.Task;
            return TokenResponse(900);
        });
        return release;
    }

    /// <summary>O próximo pedido nunca responde; só termina pelo cancelamento.</summary>
    public void Hang() =>
        _scripted.Enqueue(() => Task.Delay(Timeout.Infinite).ContinueWith(_ => TokenResponse(900)));

    public async Task WaitForRequestsAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!await _arrived.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException($"O /connect/token falso recebeu {Requests} pedido(s), esperava {count}.");
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        RawBodies.Enqueue(body);
        Forms.Enqueue(ParseForm(body));
        _arrived.Release();

        var respond = _scripted.TryDequeue(out var scripted) ? scripted : () => Task.FromResult(TokenResponse(900));
        return await respond().WaitAsync(cancellationToken);
    }

    private HttpResponseMessage TokenResponse(double expiresIn)
    {
        var token = $"token-de-servico-{Interlocked.Increment(ref _issued)}";
        Issued.Enqueue(token);
        return Json(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty);
}
