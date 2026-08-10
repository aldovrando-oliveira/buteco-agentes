using Buteco.Inbox.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Buteco.Inbox.Tests.Support;

// Mesmo mecanismo de WorkerInfrastructureFixture (apps/workers) — só
// Postgres, sem WebApplicationFactory: os testes de restart/concorrência
// (tasks.md 6.6/6.7) constroem seus próprios IHost mínimos com
// Host.CreateApplicationBuilder(), mesmo padrão de
// AgentDelegationConcurrencyTests (apps/workers).
public sealed class PostgresOnlyFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_inbox_multi_instance_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Postgres.GetConnectionString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Postgres.DisposeAsync();
}
