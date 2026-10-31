using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.Passwords;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Host para os testes de login (design.md da change usuarios-locais-login-emissor, D5),
/// com relógio controlado. O limite global de login fica <b>desligado</b> por padrão: as
/// rajadas sequenciais de um teste de bloqueio chegariam ao token bucket e virariam
/// <c>429</c> por acaso. Só o <c>LoginRateLimitTests</c> o liga, com host próprio.
/// </summary>
public class LocalUsersFixture : ApiFactoryFixture
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected virtual bool KeepsLoginRateLimit => false;

    public LocalUsersFixture() => Clock.SetLocalTimeZone(TimeZoneInfo.Local);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // O host sobe antes da migração; o aquecimento conclui logo depois dela.
            services.RemoveAll<Buteco.Api.Identity.Keys.IdentityKeyRotationSchedule>();
            services.AddSingleton(new Buteco.Api.Identity.Keys.IdentityKeyRotationSchedule { WarmupRetryInterval = TimeSpan.FromMilliseconds(200) });

            if (!KeepsLoginRateLimit)
            {
                services.PostConfigure<RateLimiterOptions>(options => options.GlobalLimiter = null);
            }
        });
    }
}

/// <summary>Usuários e login direto pela rota, sem o painel.</summary>
public static class LocalUserSeed
{
    /// <summary>A origem do issuer de teste, a que o <c>POST /auth/session</c> aceita.</summary>
    public static readonly string IssuerOrigin = new Uri(ApiFactoryFixture.PublicBaseUrl).GetLeftPart(UriPartial.Authority);

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@exemplo.test";

    public static async Task<User> CreateUserAsync(IServiceProvider services, string email, string password, string name = "Pessoa de Teste")
    {
        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = User.Create(email, name, hasher.Hash(password), scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task DeactivateAsync(IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(userId);
        user!.Deactivate(scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> PostSessionAsync(HttpClient client, string email, string password, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session")
        {
            Content = JsonContent.Create(new { email, senha = password }),
        };
        request.Headers.Add("Origin", origin ?? IssuerOrigin);
        return client.SendAsync(request);
    }
}
