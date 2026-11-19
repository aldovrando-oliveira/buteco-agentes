using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Conta todo pedido de saída de todo <c>HttpClient</c> da fábrica do host, inclusive os do cliente OIDC
/// (<c>OpenIddict.Client.SystemNetHttp</c>), e responde <c>503</c> sem ir à rede (change conectar-falha-minha-conta,
/// #281, D0, P2): é o "boot sem nenhum pedido de rede" da conferência com o proxy morto, dentro da suíte.
/// </summary>
public sealed class OutboundRequestCounter : IHttpMessageHandlerBuilderFilter
{
    private int _count;
    private readonly List<string> _hosts = [];

    public int Count => Volatile.Read(ref _count);

    /// <summary>Os hosts pedidos, para a mensagem da reprovação.</summary>
    public IReadOnlyList<string> Hosts
    {
        get
        {
            lock (_hosts)
            {
                return [.. _hosts];
            }
        }
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.AdditionalHandlers.Insert(0, new Counting(this));
    };

    private void Record(HttpRequestMessage request)
    {
        Interlocked.Increment(ref _count);
        lock (_hosts)
        {
            _hosts.Add(request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "(sem uri)");
        }
    }

    private sealed class Counting(OutboundRequestCounter counter) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            counter.Record(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request });
        }
    }
}

/// <summary>O host sem nenhum provedor, com o contador de pedidos de saída desde antes do boot e o log capturado.</summary>
public sealed class WithoutProviderCountingFixture : LocalUsersFixture
{
    public OutboundRequestCounter Outbound { get; } = new();

    public IdentityIssuerLogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(Outbound));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}
