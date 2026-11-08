using Buteco.Api.Users.Bootstrap;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.Sessions;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Users;

public static class UserServiceCollectionExtensions
{
    /// <summary>Usuários locais (design.md da change usuarios-locais-login-emissor, D2 e D4).</summary>
    public static IServiceCollection AddLocalUsers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UserBootstrapOptions>(configuration.GetSection(UserBootstrapOptions.SectionName));
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<LoginThrottleStore>();
        services.AddScoped<IUserBootstrapper, UserBootstrapper>();
        services.AddSingleton<SessionRequestGuards>();
        services.AddLoginRateLimiting();

        // As 2 etapas (design.md da change verificacao-2-etapas-totp).
        services.AddSingleton<TwoFactorSecretCipher>();
        services.AddScoped<TwoFactorVerifier>();
        services.AddScoped<TwoFactorEnrollment>();
        services.AddScoped<TwoFactorReset>();

        // As sessões sobre as tabelas do OpenIddict (design.md da change rotas-conta-usuarios, D2 e D4).
        services.AddScoped<UserSessions>();
        return services;
    }
}
