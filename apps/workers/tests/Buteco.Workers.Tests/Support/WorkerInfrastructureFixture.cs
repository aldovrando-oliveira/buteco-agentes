using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Buteco.Workers.Tests.Support;

public sealed class WorkerInfrastructureFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_workers_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder("rabbitmq:4.3-management")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Postgres.GetConnectionString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}
