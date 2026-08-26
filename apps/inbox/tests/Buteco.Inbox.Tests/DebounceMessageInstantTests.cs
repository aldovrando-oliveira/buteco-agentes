using System.Text.Json;
using A2A;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Cobre a change inbox-instante-mensagem, Tarefas 1.3/1.4: o `SendMessage`
/// disparado por `DebounceSweepService` carrega o instante de recebimento da
/// última mensagem do buffer em `Message.Metadata["messageInstant"]` (mesmo
/// padrão de fixture de <see cref="DebounceRestartAndConcurrencyTests"/> —
/// IHost independente, sem WebApplicationFactory).
/// </summary>
public class DebounceMessageInstantTests(PostgresOnlyFixture fixture) : IClassFixture<PostgresOnlyFixture>
{
    private const string MessageInstantMetadataKey = "messageInstant";

    [Fact]
    public async Task SingleMessageBuffer_DispatchedMessage_CarriesLastMessageAtAsMessageInstant()
    {
        var agentId = Guid.NewGuid();
        var a2AClientFactory = new FakeA2AClientFactory();
        // Instante fixo, sem componente sub-segundo: PendingDispatch é
        // relido do Postgres (timestamptz, precisão de microssegundo) antes
        // do disparo, não é o mesmo objeto em memória criado aqui — um
        // instante com fração de segundo arriscaria falso negativo por
        // truncamento de precisão no round-trip, não por defeito real.
        var receivedAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero).AddSeconds(-30);

        await SeedPendingDispatchAsync(agentId, "Tem lugar amanhã?", receivedAt);

        using var host = BuildHost(a2AClientFactory, window: TimeSpan.FromMilliseconds(50), sweepInterval: TimeSpan.FromMilliseconds(50));
        await host.StartAsync();

        try
        {
            await PollUntilAsync(() => Task.FromResult(a2AClientFactory.Requests.Count), count => count >= 1, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await host.StopAsync();
        }

        var request = Assert.Single(a2AClientFactory.Requests);
        var messageInstant = ExtractMessageInstant(request);

        Assert.NotNull(messageInstant);
        Assert.Equal(receivedAt, messageInstant!.Value);
    }

    [Fact]
    public async Task MultiMessageBuffer_DispatchedMessage_CarriesLastMessageInstant_NotFirst()
    {
        var agentId = Guid.NewGuid();
        var a2AClientFactory = new FakeA2AClientFactory();
        var firstReceivedAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero).AddSeconds(-40);
        var lastReceivedAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero).AddSeconds(-20);

        await SeedPendingDispatchAsync(agentId, "Tem lugar amanhã?", firstReceivedAt, appendedText: "De manhã, se der", appendedReceivedAt: lastReceivedAt);

        using var host = BuildHost(a2AClientFactory, window: TimeSpan.FromMilliseconds(50), sweepInterval: TimeSpan.FromMilliseconds(50));
        await host.StartAsync();

        try
        {
            await PollUntilAsync(() => Task.FromResult(a2AClientFactory.Requests.Count), count => count >= 1, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await host.StopAsync();
        }

        var request = Assert.Single(a2AClientFactory.Requests);
        var messageInstant = ExtractMessageInstant(request);

        Assert.NotNull(messageInstant);
        Assert.Equal(lastReceivedAt, messageInstant!.Value);
        Assert.NotEqual(firstReceivedAt, messageInstant.Value);
    }

    private static DateTimeOffset? ExtractMessageInstant(SendMessageRequest request)
    {
        if (request.Message.Metadata is null || !request.Message.Metadata.TryGetValue(MessageInstantMetadataKey, out var value))
        {
            return null;
        }

        return DateTimeOffset.Parse(value.GetString()!);
    }

    private async Task SeedPendingDispatchAsync(
        Guid agentId, string text, DateTimeOffset receivedAt, string? appendedText = null, DateTimeOffset? appendedReceivedAt = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        var contact = new Contact(channel.Id, $"+5511{Guid.NewGuid():N}"[..15], new Dictionary<string, string>(), displayName: null);
        dbContext.Contacts.Add(contact);
        var session = new Session(contact.Id);
        dbContext.Sessions.Add(session);

        var pendingDispatch = new PendingDispatch(session.Id, text, receivedAt);
        if (appendedText is not null && appendedReceivedAt is not null)
        {
            pendingDispatch.AppendMessage(appendedText, appendedReceivedAt.Value);
        }

        dbContext.PendingDispatches.Add(pendingDispatch);
        await dbContext.SaveChangesAsync();
    }

    private IHost BuildHost(FakeA2AClientFactory a2AClientFactory, TimeSpan window, TimeSpan sweepInterval)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<SessionOptions>(o => o.InactivityTimeout = TimeSpan.FromHours(1));
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
