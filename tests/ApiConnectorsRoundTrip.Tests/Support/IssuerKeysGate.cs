namespace ApiConnectorsRoundTrip.Tests.Support;

/// <summary>
/// Portão entre o cliente <c>IssuerKeys</c> do <c>apps/connectors</c> e o <c>TestServer</c>
/// do <c>apps/api</c> (change validacao-jwks-inbox-connectors, D9). Fechado, a busca de
/// chaves falha como rede fora, e o teste prova que o token continua validando sem o
/// <c>apps/api</c>. Guarda o caminho de cada busca que passou.
/// </summary>
public sealed class IssuerKeysGate
{
    public bool Closed { get; set; }

    public System.Collections.Concurrent.ConcurrentQueue<string> PassedPaths { get; } = new();
}

public sealed class IssuerKeysGateHandler(IssuerKeysGate gate) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (gate.Closed)
        {
            throw new HttpRequestException("apps/api inalcançável (portão do round-trip fechado)");
        }

        gate.PassedPaths.Enqueue(request.RequestUri!.AbsolutePath);
        return base.SendAsync(request, cancellationToken);
    }
}
