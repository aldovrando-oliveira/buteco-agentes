using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Connectors.Tests;

// Duplicado deliberadamente de
// apps/inbox/tests/Buteco.Inbox.Tests/ServiceTokenDelegatingHandlerTests.cs.

/// <summary>
/// O handler de saída do <c>apps/connectors</c> (spec <c>service-client-credentials</c>, "Token
/// anexado pelo handler de saída, e 401 do destino sem repetição"; design.md da change
/// servicos-token-emissor, D4): anexa o token do <see cref="ServiceAccessTokenSource"/>, e no
/// <c>401</c> descarta o token sem repetir a chamada. Sem contêiner. Reescrito na
/// servicos-token-emissor: antes, o caso <c>EveryRequest_CarriesAFreshServiceConnectorsToken</c>
/// afirmava um HMAC novo por requisição (mudança de asserção declarada no design, D6).
/// </summary>
public class ServiceTokenDelegatingHandlerTests
{
    private readonly FakeTokenEndpoint _endpoint = new();

    [Fact]
    public async Task Send_AttachesTheTokenFromTheSource()
    {
        var destination = new ScriptedDestination();
        using var invoker = Invoker(NewSource(), destination);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/agents/1"), CancellationToken.None);

        Assert.Equal(["token-de-servico-1"], destination.Tokens);
    }

    [Fact]
    public async Task Unauthorized_DiscardsTheToken_WithoutRetrying()
    {
        var destination = new ScriptedDestination();
        destination.Next.Enqueue(HttpStatusCode.Unauthorized);
        using var invoker = Invoker(NewSource(), destination);

        var refused = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, "http://api.test/sync/knowledge-bases/1/documents"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Single(destination.Tokens);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/agents/1"), CancellationToken.None);
        Assert.Equal(["token-de-servico-1", "token-de-servico-2"], destination.Tokens);
    }

    [Fact]
    public async Task ALateUnauthorized_ForATokenAlreadyReplaced_KeepsTheNewOne()
    {
        var destination = new ScriptedDestination();
        using var invoker = Invoker(NewSource(), destination);

        // A sai com T1 e fica segurada; B sai com T1 e recebe 401, que troca o cache por T2.
        var held = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        destination.Held.Enqueue(held.Task);
        var late = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/a"), CancellationToken.None);
        await destination.WaitForAsync(1);

        destination.Next.Enqueue(HttpStatusCode.Unauthorized);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/b"), CancellationToken.None);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/c"), CancellationToken.None);

        // O 401 de A chega depois, para o T1 já trocado.
        held.SetResult(HttpStatusCode.Unauthorized);
        await late;
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.test/d"), CancellationToken.None);

        Assert.Equal(["token-de-servico-1", "token-de-servico-1", "token-de-servico-2", "token-de-servico-2"], destination.Tokens);
        Assert.Equal(2, _endpoint.Requests);
    }

    private ServiceAccessTokenSource NewSource() =>
        new(
            new SingleClientFactory(_endpoint),
            Microsoft.Extensions.Options.Options.Create(new ServiceClientOptions { Secret = TestAuthentication.ServiceClientSecret }),
            new ServiceAccessTokenSchedule(),
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            NullLogger<ServiceAccessTokenSource>.Instance);

    private static HttpMessageInvoker Invoker(ServiceAccessTokenSource source, HttpMessageHandler destination) =>
        new(new ServiceTokenDelegatingHandler(source) { InnerHandler = destination });

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://api-interno.test"), Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// O destino: grava o token de cada chamada, responde o próximo status da fila (ou
    /// <c>200</c>) e, se houver, segura a resposta até o teste soltar.
    /// </summary>
    private sealed class ScriptedDestination : HttpMessageHandler
    {
        private readonly SemaphoreSlim _arrived = new(0);

        public List<string> Tokens { get; } = [];

        public Queue<HttpStatusCode> Next { get; } = new();

        public Queue<Task<HttpStatusCode>> Held { get; } = new();

        public async Task WaitForAsync(int calls)
        {
            for (var i = 0; i < calls; i++)
            {
                Assert.True(await _arrived.WaitAsync(TimeSpan.FromSeconds(10)));
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Task<HttpStatusCode>? held;
            HttpStatusCode? next;
            lock (Tokens)
            {
                Tokens.Add(request.Headers.Authorization!.Parameter!);
                held = Held.Count > 0 ? Held.Dequeue() : null;
                next = held is null && Next.Count > 0 ? Next.Dequeue() : null;
            }

            _arrived.Release();
            var status = held is not null ? await held : next ?? HttpStatusCode.OK;
            return new HttpResponseMessage(status);
        }
    }
}
