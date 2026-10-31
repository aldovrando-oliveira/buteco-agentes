using Microsoft.AspNetCore.Authentication.Cookies;

namespace Buteco.Api.Identity.Flow;

/// <summary>
/// A sessão do emissor (design.md da change usuarios-locais-login-emissor, D1): um cookie
/// curto que só existe para a passagem do login ao authorize, e mais tarde para o passo das
/// 2 etapas (#218). A sessão de verdade é a autorização do OpenIddict, com o refresh token
/// do painel.
///
/// <list type="bullet">
/// <item><c>HttpOnly</c>, <c>SameSite=Strict</c>, <c>Path=/</c>, sem <c>Domain</c>;</item>
/// <item><c>Secure</c> sempre fora de <c>Development</c>: o TLS termina fora do stack e o
/// <c>apps/api</c> vê HTTP, então <c>SameAsRequest</c> nunca marcaria em produção;</item>
/// <item>5 minutos, sem renovação;</item>
/// <item>carrega o id do usuário e o <c>security_stamp</c>, e nada mais.</item>
/// </list>
/// </summary>
public static class IssuerSession
{
    public const string SchemeName = "ButecoSession";

    public const string CookieName = "buteco-sessao";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>Claim do id do usuário no cookie.</summary>
    public const string UserIdClaim = "sub";

    /// <summary>Claim do carimbo de segurança, no cookie, no código e no refresh token.</summary>
    public const string SecurityStampClaim = "buteco_stamp";

    public static IServiceCollection AddIssuerSessionCookie(this IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(SchemeName, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = Lifetime;
            options.SlidingExpiration = false;

            // Nenhuma rota desafia este esquema: quem o lê (o authorize) decide sozinho para
            // onde mandar. Sem isto, um desafio viraria 302 para /Account/Login.
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                },
            };
        });

        // O ambiente só existe no contêiner, e o registro não o recebe.
        services.AddOptions<CookieAuthenticationOptions>(SchemeName).Configure<IHostEnvironment>((options, environment) =>
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);

        return services;
    }
}
