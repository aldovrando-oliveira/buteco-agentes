using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Login;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O ciclo horário da limpeza remove as contagens de login paradas (spec
/// <c>local-users</c>, "Contagem parada é limpa"; design.md da change
/// usuarios-locais-login-emissor, D4): sem falha há mais de 24 h e fora de bloqueio. Classe
/// própria, e não um caso do <c>IdentityTokenPruneTests</c>: aquele é um teste só em
/// passos, com o relógio da fixture andando, e uma contagem a mais mudaria o número dele.
/// </summary>
public class LoginThrottlePruneTests(LoginThrottlePruneTests.Fixture factory) : IClassFixture<LoginThrottlePruneTests.Fixture>
{
    public sealed class Fixture : ApiFactoryFixture
    {
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

        public Fixture() => Clock.SetLocalTimeZone(TimeZoneInfo.Local);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IdentityTokenPruneSchedule>();
                services.AddSingleton(new IdentityTokenPruneSchedule { Interval = Timeout.InfiniteTimeSpan });
            });
        }
    }

    [Fact]
    public async Task Prune_RemovesOnlyStoppedCounts()
    {
        var now = factory.Clock.GetUtcNow();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LoginThrottles.AddRange(
                new LoginThrottle { NormalizedEmail = "parada@exemplo.test", ConsecutiveFailures = 3, LastFailureAt = now - TimeSpan.FromHours(25) },
                new LoginThrottle { NormalizedEmail = "recente@exemplo.test", ConsecutiveFailures = 3, LastFailureAt = now - TimeSpan.FromHours(23) },
                new LoginThrottle { NormalizedEmail = "vencida@exemplo.test", LastLockMinutes = 60, LockedUntil = now - TimeSpan.FromHours(24), LastFailureAt = now - TimeSpan.FromHours(25) });
            await db.SaveChangesAsync();
        }

        var result = await factory.Services.GetRequiredService<IIdentityTokenPruner>().RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, result.LoginThrottles);
        using var check = factory.Services.CreateScope();
        var remaining = await check.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles
            .Select(throttle => throttle.NormalizedEmail).ToListAsync();
        Assert.Equal(["recente@exemplo.test"], remaining);
    }
}
