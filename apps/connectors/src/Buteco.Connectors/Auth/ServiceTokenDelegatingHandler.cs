using System.Net.Http.Headers;

namespace Buteco.Connectors.Auth;

// Assina um token de serviço novo a cada requisição de saída para apps/api, com sub
// "service:connectors" (design.md da change ciclo-de-sincronizacao, D1). Não guarda nem
// reaproveita token entre chamadas.
//
// TERCEIRA CÓPIA do molde, com o subject trocado, sem libs/ (a extração ficou como
// alternativa da #117, que vai redesenhar a assinatura): as outras duas são
// apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs (service:inbox) e
// apps/api/src/Buteco.Api/Auth/ServiceTokenDelegatingHandler.cs (service:api). Quem aceita
// este subject é a tabela de apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs,
// só nas cinco rotas de /sync/knowledge-bases. Mudar um dos três exige olhar os outros dois.
//
// ConnectorsSubject NÃO entra na tabela de ConnectorsSubjectAuthorizationHandler: aquela
// tabela diz quem pode chamar este app, e um token service:connectors que chegue aqui
// recebe 403, como qualquer subject desconhecido.
public sealed class ServiceTokenDelegatingHandler(ITokenService tokenService) : DelegatingHandler
{
    public const string ConnectorsSubject = "service:connectors";

    // TTL fixo, não configurável, pelo mesmo motivo dos outros dois: o token nunca sai
    // do processo nem é visto por um humano, só precisa sobreviver à rede e ao
    // processamento até o apps/api validar a assinatura.
    private static readonly TimeSpan ServiceTokenLifetime = TimeSpan.FromMinutes(5);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (token, _) = tokenService.Issue(ConnectorsSubject, ServiceTokenLifetime);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
