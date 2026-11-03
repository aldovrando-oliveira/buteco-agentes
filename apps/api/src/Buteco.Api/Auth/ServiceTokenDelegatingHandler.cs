using System.Net;
using System.Net.Http.Headers;
using Buteco.Api.Identity.ServiceTokens;

namespace Buteco.Api.Auth;

// Anexa a toda requisição de saída para apps/connectors o access token que o apps/api gera
// no processo, com client_id buteco-servico-api e aud buteco-connectors (design.md da change
// servicos-token-emissor, D2 e D4). O subject service:api sai do client_id, pela tabela de
// clientes do apps/connectors (apps/connectors/src/Buteco.Connectors/Auth/IssuerClientSubjects.cs),
// e quem decide as rotas dele é
// apps/connectors/src/Buteco.Connectors/Auth/ConnectorsSubjectAuthorizationHandler.cs.
//
// O mesmo comportamento diante do destino que os handlers do apps/inbox e do apps/connectors:
// 401 descarta o token em cache, sem repetir a chamada. Repetir duplicaria uma chamada que
// já tenha produzido efeito.
public sealed class ServiceTokenDelegatingHandler(ServiceAccessTokenIssuer issuer) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await issuer.GetAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            issuer.Invalidate(token);
        }

        return response;
    }
}
