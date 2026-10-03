using System.Net.Http.Headers;

namespace Buteco.Api.Auth;

// Assina um token de serviço novo a cada requisição de saída para apps/connectors,
// com sub "service:api" (design.md da change criacao-base-sincronizada, D4). Não
// guarda nem reaproveita token entre chamadas.
//
// ESPELHO de apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs, com
// o subject trocado, sem libs/ (a extração ficou registrada como alternativa da
// #117). Quem aceita este subject é a tabela de
// apps/connectors/src/Buteco.Connectors/Auth/ConnectorsSubjectAuthorizationHandler.cs,
// só na rota de descrição de pasta. Mudar um dos três exige olhar os outros dois.
//
// ApiSubject NÃO entra na tabela de ServiceScopeAuthorizationHandler: aquela tabela
// diz quem pode chamar o apps/api, e um token service:api que chegue aqui recebe
// 403, como qualquer subject desconhecido.
public sealed class ServiceTokenDelegatingHandler(ITokenService tokenService) : DelegatingHandler
{
    public const string ApiSubject = "service:api";

    // TTL fixo, não configurável, pelo mesmo motivo do apps/inbox: o token nunca sai
    // do processo nem é visto por um humano, só precisa sobreviver à rede e ao
    // processamento até o apps/connectors validar a assinatura.
    private static readonly TimeSpan ServiceTokenLifetime = TimeSpan.FromMinutes(5);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (token, _) = tokenService.Issue(ApiSubject, ServiceTokenLifetime);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
