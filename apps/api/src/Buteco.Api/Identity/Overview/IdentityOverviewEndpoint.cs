using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;

namespace Buteco.Api.Identity.Overview;

/// <summary>
/// <c>GET /identity</c>: o emissor e as chaves de assinatura para a tela Identidade (#211;
/// design.md da change leitura-identidade-emissor, D1 a D7).
/// <list type="bullet">
/// <item><b>Acesso:</b> só Admin (design.md da change papeis-no-token, D1): o <c>role</c> do token
/// pela tabela de papéis, e o papel gravado pela <see cref="CurrentUser.FindAdminAsync"/>. Token de
/// serviço recebe <c>403</c> da política de fallback, e o token sem usuário ou de quem não é Admin,
/// da <see cref="CurrentUser"/>.</item>
/// <item><b>Endereços:</b> do issuer configurado, como o discovery os monta
/// (<see cref="DiscoveryEndpointsFromIssuerHandler"/>), nunca do <c>Host</c> da requisição.</item>
/// <item><b>Durações:</b> de <see cref="IdentityTokenLifetimes"/> e da
/// <see cref="IdentityKeyRotationPolicy"/>, as mesmas constantes que o emissor usa.</item>
/// <item><b>Chaves:</b> pela <see cref="IdentityKeyRotationPolicy.Describe"/>, no instante da
/// requisição. A troca real acontece em até um <see cref="IdentityKeyRotationPolicy.CheckInterval"/>
/// depois da data, e é isso que <c>verificacaoSegundos</c> informa.</item>
/// </list>
/// Sem estado "emissor desligado": sem url pública o boot reprova (D5). Sem campo "operando":
/// com o anel fora, o gate responde <c>503</c> antes desta rota.
/// </summary>
public static class IdentityOverviewEndpoint
{
    public const string Path = "/identity";

    public static IEndpointRouteBuilder MapIdentityOverviewEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, GetAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(
        HttpContext httpContext,
        AppDbContext dbContext,
        IdentityIssuerState issuerState,
        IdentityKeyRing ring,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var issuer = issuerState.Issuer
            ?? throw new InvalidOperationException("GET /identity sem issuer: o boot deveria ter reprovado a url pública ausente.");

        var keys = await ring.ListSigningKeysAsync(cancellationToken);
        var described = IdentityKeyRotationPolicy.Describe(keys, timeProvider.GetUtcNow());

        return TypedResults.Ok(new IdentityOverviewResponse(
            new IssuerOverview(
                issuer.AbsoluteUri,
                Absolute(issuer, IdentityIssuerEndpoints.Configuration),
                Absolute(issuer, IdentityIssuerEndpoints.JsonWebKeySet),
                Absolute(issuer, IdentityIssuerEndpoints.Token),
                Seconds(IdentityTokenLifetimes.AccessToken),
                Seconds(IdentityTokenLifetimes.RefreshToken)),
            new SigningKeysOverview(
                Seconds(IdentityKeyRotationPolicy.RotationInterval),
                Seconds(IdentityKeyRotationPolicy.PrePublication),
                Seconds(IdentityKeyRotationPolicy.Retention),
                Seconds(IdentityKeyRotationPolicy.CheckInterval),
                described.Select(SigningKeyOverview.From).ToList())));
    }

    // O issuer termina em barra e os endereços são relativos sem a barra inicial: a mesma
    // composição que o discovery faz (CreateAbsoluteUri sobre o issuer).
    private static string Absolute(Uri issuer, string path) => new Uri(issuer, path.TrimStart('/')).AbsoluteUri;

    private static long Seconds(TimeSpan duration) => (long)duration.TotalSeconds;
}
