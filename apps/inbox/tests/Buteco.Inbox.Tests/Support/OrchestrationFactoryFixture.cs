using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// Mesmo mecanismo de <see cref="InboxFactoryFixture"/> (Postgres efêmero
/// via Testcontainers, sem rede real para apps/api), mais especificamente
/// para os testes do orquestrador: <see cref="IA2AClientFactory"/>
/// substituído por <see cref="FakeA2AClientFactory"/>, e
/// <c>DebounceOptions</c> com janela/intervalo de varredura curtos para os
/// testes rodarem rápido sem abstração de relógio (nenhuma existe no
/// projeto — mesmo padrão de <c>ContactSessionResolverTests</c>).
/// </summary>
public sealed class OrchestrationFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_inbox_orchestration_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeA2AClientFactory A2AClientFactory { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                ["Api:BaseUrl"] = "http://apps-api.test",
                ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
                ["Debounce:Window"] = "00:00:00.300",
                ["Debounce:SweepInterval"] = "00:00:00.050",
                ["Debounce:MaxDispatchAttempts"] = "3",
            });
            config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

            var a2AClientFactoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IA2AClientFactory));
            if (a2AClientFactoryDescriptor is not null)
            {
                services.Remove(a2AClientFactoryDescriptor);
            }

            services.AddSingleton<IA2AClientFactory>(A2AClientFactory);
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestAuthentication.AttachOperatorToken(client, Services);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migra com um AppDbContext isolado, sem tocar Services — acessar
        // Services aqui já constrói e INICIA o host (WebApplicationFactory),
        // incluindo DebounceSweepService via AddHostedService. Com
        // Debounce:SweepInterval em 50ms (acima), o primeiro sweep pode
        // consultar pending_dispatches antes desta migration terminar,
        // lançando uma exceção não tratada dentro do BackgroundService —
        // que em .NET 8+ derruba o host inteiro (HostOptions.BackgroundServiceExceptionBehavior
        // = StopHost por padrão), descartando o IServiceProvider antes de
        // qualquer teste rodar. Mesmo padrão de PostgresOnlyFixture: migration
        // via um AppDbContext próprio, options apontando direto pra
        // connection string do Testcontainers.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
