using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Fonte 1 da #47 na parada normal (design.md, D11): o trecho entre o claim e a
/// gravação do TaskId não é interrompido pela parada, que o espera até o prazo.
/// </summary>
public class DispatchShutdownTests(PostgresOnlyFixture fixture) : IClassFixture<PostgresOnlyFixture>
{
    /// <summary>
    /// apps/api que não responde: a parada espera a unidade até o prazo dela, e
    /// não além (design.md, D11). A linha fica sem TaskId, que é o caso do D7.
    /// </summary>
    [Fact]
    public async Task Source1_InboxStopsWithApiNotResponding_StopIsBoundedByTheUnitDeadline()
    {
        var agentId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a2AClientFactory = new FakeA2AClientFactory
        {
            BeforeReturn = async (_, _, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            },
        };

        Guid sessionId;
        TimeSpan stopDuration;
        using (var host = BuildHost(a2AClientFactory))
        {
            await host.StartAsync();
            sessionId = await SeedAndReceiveAsync(host.Services, agentId);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await host.StopAsync();
            stopDuration = stopwatch.Elapsed;
        }

        Console.WriteLine($"MEDIDA parada={stopDuration.TotalMilliseconds:F0} ms");
        Assert.True(stopDuration < TimeSpan.FromSeconds(10), $"parada levou {stopDuration}");
        var dispatch = await FindDispatchAsync(sessionId);
        Assert.NotNull(dispatch);
        Assert.Equal(PendingDispatchStatus.Dispatching, dispatch.Status);
        Assert.Null(dispatch.TaskId);
    }

    [Fact]
    public async Task Source1_InboxStopsWithSendMessageInFlight_StopWaitsAndTaskIdIsRecorded()
    {
        var agentId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a2AClientFactory = new FakeA2AClientFactory
        {
            // apps/api leva 1 s para responder, e a parada chega no meio.
            BeforeReturn = async (_, _, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            },
        };

        Guid sessionId;
        TimeSpan stopDuration;
        using (var host = BuildHost(a2AClientFactory))
        {
            await host.StartAsync();
            sessionId = await SeedAndReceiveAsync(host.Services, agentId);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await host.StopAsync();
            stopDuration = stopwatch.Elapsed;
        }

        Console.WriteLine($"MEDIDA parada={stopDuration.TotalMilliseconds:F0} ms");
        var dispatch = await FindDispatchAsync(sessionId);
        Assert.NotNull(dispatch);
        Assert.Equal(PendingDispatchStatus.Dispatching, dispatch.Status);
        Assert.NotNull(dispatch.TaskId);
        Assert.True(stopDuration < TimeSpan.FromSeconds(10), $"parada levou {stopDuration}");
    }

    private IHost BuildHost(FakeA2AClientFactory a2AClientFactory)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Configuration["PublicUrl:BaseUrl"] = "http://apps-inbox.test";
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<SessionOptions>(options => options.InactivityTimeout = TimeSpan.FromHours(1));
        builder.Services.Configure<Buteco.Inbox.Options.PublicUrlOptions>(options => options.BaseUrl = "http://apps-inbox.test");
        builder.Services.Configure<DebounceOptions>(options =>
        {
            options.Window = TimeSpan.FromMilliseconds(200);
            options.SweepInterval = TimeSpan.FromMilliseconds(50);
            options.MaxDispatchAttempts = 3;
        });

        builder.Services.AddScoped<IContactSessionResolver, ContactSessionResolver>();
        builder.Services.AddScoped<IInboundMessageOrchestrator, InboundMessageOrchestrator>();
        builder.Services.AddSingleton<IA2AClientFactory>(a2AClientFactory);
        builder.Services.AddHostedService<DebounceSweepService>();

        return builder.Build();
    }

    private static async Task<Guid> SeedAndReceiveAsync(IServiceProvider services, Guid agentId)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        var externalId = $"+5511{Guid.NewGuid():N}"[..15];
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channel.Id, externalId, "Olá", MessageContentType.Text, Guid.NewGuid().ToString(),
            displayName: null, DateTimeOffset.UtcNow, new Dictionary<string, string>(), CancellationToken.None);

        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channel.Id);
        return await dbContext.Sessions.AsNoTracking().Where(s => s.ContactId == contact.Id).Select(s => s.Id).SingleAsync();
    }

    private async Task<PendingDispatch?> FindDispatchAsync(Guid sessionId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }
}
