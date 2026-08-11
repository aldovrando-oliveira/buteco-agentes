using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Buteco.Inbox.Tests;

// Constrói instâncias de IHost independentes (mesmo padrão de
// AgentDelegationConcurrencyTests, apps/workers) — sem WebApplicationFactory,
// só o necessário para exercitar IInboundMessageOrchestrator e
// DebounceSweepService de verdade contra o mesmo Postgres.
public class DebounceRestartAndConcurrencyTests(PostgresOnlyFixture fixture) : IClassFixture<PostgresOnlyFixture>
{
    [Fact]
    public async Task PendingDispatch_SurvivesRestart_AndDispatchesAfterNewHostStarts()
    {
        var agentId = Guid.NewGuid();
        var a2AClientFactory = new FakeA2AClientFactory();

        // "Instância" 1: cria o buffer com uma janela deliberadamente
        // longa, depois é parada — prova que nada disparou nela (design.md,
        // Decisão 1: o buffer não é perdido nem processado prematuramente).
        using (var firstInstance = BuildHost(a2AClientFactory, window: TimeSpan.FromSeconds(30), sweepInterval: TimeSpan.FromMilliseconds(100)))
        {
            await firstInstance.StartAsync();
            await SeedAndReceiveAsync(firstInstance.Services, agentId, "Mensagem antes do restart");
            await firstInstance.StopAsync();
        }

        Assert.Empty(a2AClientFactory.Requests);

        // "Instância" 2: processo novo, mesmo Postgres, janela curta —
        // prova que o buffer sobreviveu ao restart e dispara normalmente.
        using var secondInstance = BuildHost(a2AClientFactory, window: TimeSpan.FromMilliseconds(200), sweepInterval: TimeSpan.FromMilliseconds(50));
        await secondInstance.StartAsync();
        try
        {
            await PollUntilAsync(
                () => Task.FromResult(a2AClientFactory.RequestedAgentIds.Count(id => id == agentId)),
                count => count >= 1,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await secondInstance.StopAsync();
        }
    }

    [Fact]
    public async Task TwoInstances_CompetingForSameExpiredPendingDispatch_OnlyOneDispatches()
    {
        var agentId = Guid.NewGuid();
        var a2AClientFactory = new FakeA2AClientFactory();

        // PendingDispatch já "vencida" criada direto no banco (LastMessageAt
        // no passado) — sem depender de uma janela real decorrendo, mesmo
        // mecanismo de backdate via SQL de ContactSessionResolverTests
        // (nenhuma abstração de relógio existe no projeto).
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
            await using var dbContext = new AppDbContext(options);

            var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
            dbContext.Channels.Add(channel);
            var contact = new Contact(channel.Id, $"+5511{Guid.NewGuid():N}"[..15], new Dictionary<string, string>());
            dbContext.Contacts.Add(contact);
            var session = new Session(contact.Id);
            dbContext.Sessions.Add(session);
            var pendingDispatch = new PendingDispatch(session.Id, "Mensagem concorrida", DateTimeOffset.UtcNow.AddSeconds(-30));
            dbContext.PendingDispatches.Add(pendingDispatch);
            await dbContext.SaveChangesAsync();
        }

        // Instância A e instância B, configuradas de forma idêntica —
        // ambas podem reivindicar a mesma PendingDispatch, só uma pode
        // vencer o claim otimista via xmin (design.md, Decisão 5).
        using var instanceA = BuildHost(a2AClientFactory, window: TimeSpan.FromSeconds(1), sweepInterval: TimeSpan.FromMilliseconds(20));
        using var instanceB = BuildHost(a2AClientFactory, window: TimeSpan.FromSeconds(1), sweepInterval: TimeSpan.FromMilliseconds(20));

        await instanceA.StartAsync();
        await instanceB.StartAsync();

        try
        {
            await PollUntilAsync(
                () => Task.FromResult(a2AClientFactory.RequestedAgentIds.Count(id => id == agentId)),
                count => count >= 1,
                TimeSpan.FromSeconds(5));

            // Janela curta o suficiente pra um disparo duplicado (se o claim
            // não fosse idempotente) já ter acontecido.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            await instanceA.StopAsync();
            await instanceB.StopAsync();
        }

        Assert.Equal(1, a2AClientFactory.RequestedAgentIds.Count(id => id == agentId));
    }

    private IHost BuildHost(FakeA2AClientFactory a2AClientFactory, TimeSpan window, TimeSpan sweepInterval)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<Buteco.Inbox.Contacts.SessionOptions>(options => options.InactivityTimeout = TimeSpan.FromHours(1));
        builder.Services.Configure<DebounceOptions>(options =>
        {
            options.Window = window;
            options.SweepInterval = sweepInterval;
            options.MaxDispatchAttempts = 3;
        });

        builder.Services.AddScoped<IContactSessionResolver, ContactSessionResolver>();
        builder.Services.AddScoped<IInboundMessageOrchestrator, InboundMessageOrchestrator>();
        builder.Services.AddSingleton<IA2AClientFactory>(a2AClientFactory);
        builder.Services.AddHostedService<DebounceSweepService>();

        return builder.Build();
    }

    private async Task SeedAndReceiveAsync(IServiceProvider services, Guid agentId, string text)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        var externalId = $"+5511{Guid.NewGuid():N}"[..15];
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(channel.Id, externalId, text, DateTimeOffset.UtcNow, new Dictionary<string, string>(), CancellationToken.None);
    }

    private static async Task<T> PollUntilAsync<T>(Func<Task<T>> probeAsync, Func<T, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = await probeAsync();
            if (isDone(value))
            {
                return value;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }
}
