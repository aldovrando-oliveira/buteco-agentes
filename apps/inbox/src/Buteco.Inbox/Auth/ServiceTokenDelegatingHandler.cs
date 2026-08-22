using System.Net.Http.Headers;

namespace Buteco.Inbox.Auth;

// Assina um token de serviço novo a cada requisição de saída para
// apps/api — não guarda nem reaproveita nenhum token entre chamadas
// (design.md, Decision 3). Custo de assinar é uma HMACSHA256 em
// memória, desprezível; não há cache para invalidar nem refresh para
// acertar.
public sealed class ServiceTokenDelegatingHandler(ITokenService tokenService) : DelegatingHandler
{
    public const string ServiceSubject = "service:inbox";

    // TTL fixo, não configurável (design.md, Decision 3) — o token
    // nunca sai do processo nem é observado por um humano: só precisa
    // sobreviver ao tempo de rede + processamento até apps/api validar
    // a assinatura. Diferente do TTL do operador (Auth:OperatorTokenLifetime),
    // não há decisão de produto por trás deste valor que justifique
    // torná-lo configurável por ambiente.
    private static readonly TimeSpan ServiceTokenLifetime = TimeSpan.FromMinutes(5);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (token, _) = tokenService.Issue(ServiceSubject, ServiceTokenLifetime);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
