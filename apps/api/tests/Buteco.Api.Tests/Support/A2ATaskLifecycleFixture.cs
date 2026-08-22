using Buteco.Api.Infrastructure;
using Buteco.Api.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Buteco.Api.Tests.Support;

public sealed class A2ATaskLifecycleFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_agents_a2a_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.3-management")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public RabbitMqContainer RabbitMq => _rabbitMq;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(TestAuthentication.ConfigOverrides));

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

            services.Configure<RabbitMqOptions>(options =>
            {
                options.Host = _rabbitMq.Hostname;
                options.Port = _rabbitMq.GetMappedPublicPort(5672);
                options.Username = "buteco";
                options.Password = "buteco_test_password";
            });
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestAuthentication.AttachOperatorToken(client, Services);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitMq.DisposeAsync();
    }
}
