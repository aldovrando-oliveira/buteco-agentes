using Buteco.Api.Users.Bootstrap;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;

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
        return services;
    }
}
