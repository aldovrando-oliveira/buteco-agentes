using System.Net;
using System.Net.Http.Headers;

namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/ServiceTokenDelegatingHandler.cs — sem libs/
// (design.md da change servicos-token-emissor, D4). O handler do apps/api faz o mesmo com o
// token gerado no processo.

/// <summary>
/// Anexa a toda requisição de saída para o <c>apps/api</c> o token de serviço do emissor
/// (design.md da change servicos-token-emissor, D4). O subject <c>service:inbox</c> sai do
/// <c>client_id</c> <c>buteco-servico-inbox</c>, pela tabela de clientes do <c>apps/api</c>.
/// <para>
/// <b><c>401</c> do destino:</b> descarta o token em cache e devolve a resposta, sem repetir.
/// Repetir duplicaria um <c>POST /agents/{id}/a2a</c> que já tenha produzido efeito.
/// </para>
/// </summary>
public sealed class ServiceTokenDelegatingHandler(ServiceAccessTokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens.Invalidate(token);
        }

        return response;
    }
}
