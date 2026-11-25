using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Um fornecedor de LLM falso, como handler primário do <c>HttpClient</c> nomeado da listagem
/// de modelos de um provedor (design.md da change modelos-curados-por-provedor, D4 e a tarefa
/// 3.4). Registra cada requisição (URL e cabeçalhos, copiados na chegada) e responde por
/// <see cref="Respond"/>.
///
/// <para>
/// <b>Os corpos são forjados aqui</b>, copiados da documentação de 19/11/2026 (Anthropic e
/// Gemini) e da resposta real gravada do gateway compatível (OpenAI,
/// <c>{"object":"list","data":[]}</c>). A conferência com resposta real de Anthropic e Gemini
/// é a tarefa 3.7, fora da suíte (R3).
/// </para>
/// </summary>
public sealed class FakeVendorModelsHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<VendorRequest> _requests = new();

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"object":"list","data":[]}"""));

    public IReadOnlyList<VendorRequest> Requests => [.. _requests];

    public int RequestCount => _requests.Count;

    public void Reset(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _requests.Clear();
        Respond = respond;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Responde sempre o mesmo, a cada requisição.</summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Always(HttpStatusCode status, string body) =>
        (_, _) => Task.FromResult(Json(status, body));

    /// <summary>Nunca responde: segura até o cancelamento (o orçamento ou o timeout da página).</summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Never() =>
        async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("inalcançável");
        };

    /// <summary>Falha de conexão, como a de um fornecedor fora do ar.</summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Unreachable() =>
        (_, _) => throw new HttpRequestException("conexão recusada (fornecedor falso)");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(new VendorRequest(
            request.RequestUri!,
            request.Headers.ToDictionary(header => header.Key.ToLowerInvariant(), header => string.Join(",", header.Value), StringComparer.Ordinal)));

        return await Respond(request, cancellationToken);
    }
}

public sealed record VendorRequest(Uri Uri, IReadOnlyDictionary<string, string> Headers);
