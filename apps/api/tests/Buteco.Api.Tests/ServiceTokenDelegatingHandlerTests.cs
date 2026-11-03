using System.Net;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.ServiceTokens;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests;

/// <summary>
/// O token que o <c>apps/api</c> anexa às chamadas ao <c>apps/connectors</c> (spec
/// <c>knowledge-sync-folder-validation</c>, "Token de serviço service:api nas chamadas ao
/// apps/connectors"; design.md da change servicos-token-emissor, D2 e D4): o do
/// <see cref="ServiceAccessTokenIssuer"/>, reaproveitado entre requisições, e descartado no
/// <c>401</c> do destino sem repetir a chamada. Reescrito na servicos-token-emissor: antes,
/// era um HMAC novo por requisição (mudança de asserção declarada no design, D6).
/// </summary>
public class ServiceTokenDelegatingHandlerTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>, IAsyncLifetime
{
    public async Task InitializeAsync() =>
        Assert.True((await factory.CreateClient().GetAsync("/.well-known/jwks")).IsSuccessStatusCode);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Send_AttachesTheInProcessToken_AndReusesItAcrossRequests()
    {
        var destination = new ScriptedHandler();
        using var invoker = Invoker(NewIssuer(), destination);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/a"), CancellationToken.None);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/b"), CancellationToken.None);

        Assert.Equal(2, destination.Tokens.Count);
        Assert.Equal(destination.Tokens[0], destination.Tokens[1]);
        var token = new JsonWebToken(destination.Tokens[0]);
        Assert.Equal("buteco-servico-api", token.GetPayloadValue<string>(Claims.ClientId));
        Assert.Equal([IdentityClients.ConnectorsScope], token.Audiences);
    }

    [Fact]
    public async Task Unauthorized_DiscardsTheToken_WithoutRetrying()
    {
        var destination = new ScriptedHandler();
        destination.Next.Enqueue(HttpStatusCode.Unauthorized);
        using var invoker = Invoker(NewIssuer(), destination);

        var refused = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/a"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Single(destination.Tokens);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/b"), CancellationToken.None);
        Assert.Equal(2, destination.Tokens.Count);
        Assert.NotEqual(destination.Tokens[0], destination.Tokens[1]);
    }

    [Fact]
    public async Task ALateUnauthorized_ForATokenAlreadyReplaced_KeepsTheNewOne()
    {
        var issuer = NewIssuer();
        var destination = new ScriptedHandler();
        using var invoker = Invoker(issuer, destination);

        // A sai com T1 e fica segurada; B sai com T1 e recebe 401, que troca o cache por T2.
        var held = new TaskCompletionSource<HttpStatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        destination.Held.Enqueue(held.Task);
        var late = invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/a"), CancellationToken.None);
        await destination.WaitForAsync(1);

        destination.Next.Enqueue(HttpStatusCode.Unauthorized);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/b"), CancellationToken.None);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/c"), CancellationToken.None);
        var replacement = destination.Tokens[2];
        Assert.NotEqual(destination.Tokens[0], replacement);

        // O 401 de A chega depois, para o T1 já trocado.
        held.SetResult(HttpStatusCode.Unauthorized);
        await late;

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/d"), CancellationToken.None);
        Assert.Equal(replacement, destination.Tokens[3]);
    }

    private ServiceAccessTokenIssuer NewIssuer() =>
        ActivatorUtilities.CreateInstance<ServiceAccessTokenIssuer>(factory.Services);

    private static HttpMessageInvoker Invoker(ServiceAccessTokenIssuer issuer, HttpMessageHandler destination) =>
        new(new ServiceTokenDelegatingHandler(issuer) { InnerHandler = destination });

    /// <summary>
    /// O destino: grava o token de cada chamada, responde o próximo status da fila (ou
    /// <c>200</c>) e, se houver, segura a resposta até o teste soltar.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
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
