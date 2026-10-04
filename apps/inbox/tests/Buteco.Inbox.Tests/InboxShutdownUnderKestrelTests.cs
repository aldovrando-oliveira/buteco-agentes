using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Parada do apps/inbox real, sob Kestrel, com trabalho em voo (#47, design.md
/// D11, condição do portão 1). A ordem real de parada é inversa ao registro e
/// em série, e o GenericWebHostService (Kestrel) é registrado no Build(), depois
/// de todo AddHostedService: ele para PRIMEIRO e espera as requisições em voo,
/// enquanto o stoppingToken dos outros serviços ainda não foi cancelado. O
/// orçamento é o stop_grace_period padrão do Compose, 10 s.
/// </summary>
public class InboxShutdownUnderKestrelTests(PostgresOnlyFixture fixture, ITestOutputHelper output) : IClassFixture<PostgresOnlyFixture>
{
    private static readonly TimeSpan ComposeStopGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Mensagem cuja janela vence DURANTE a espera do Kestrel por um push em voo
    /// não é reivindicada: sem isso, a unidade de 8 s aberta depois do pedido de
    /// parada a empurrava para 10,9 s (medido em 04/10/2026).
    /// </summary>
    [Fact]
    public async Task PendingThatMaturesWhileKestrelWaits_IsNotClaimed_AndStopFitsTheGracePeriod()
    {
        var log = new Timeline();
        var a2a = new FakeA2AClientFactory
        {
            BeforeReturn = async (_, _, token) =>
            {
                log.Mark("debounce: SendMessage começou");
                await Task.Delay(Timeout.Infinite, token);
            },
        };
        var sender = new DelayedSender(TimeSpan.FromSeconds(6), log);

        await using var factory = Start(a2a, sender, log, window: "00:00:03");
        var client = factory.CreateClient();

        var row = await SeedDispatchingAsync(factory.Services, taskId: Guid.NewGuid().ToString("N"));
        var push = PostPushAsync(client, AgentTaskOf(row.TaskId!, "resposta"), row.Token);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var pending = await ReceiveAsync(factory.Services);

        var stop = await StopAsync(factory, log);
        await push;
        log.Print(output);

        Assert.True(stop < ComposeStopGracePeriod, $"parada levou {stop}");
        Assert.Empty(a2a.Requests);
        var dispatch = await FindDispatchAsync(pending);
        Assert.Equal(PendingDispatchStatus.Pending, dispatch!.Status);
    }

    [Fact]
    public async Task ReconciliationDeliveryInFlight_WithinTheDeadline_IsAwaited_AndTheRowIsRemoved()
    {
        var log = new Timeline();
        var a2a = new FakeA2AClientFactory();
        var sender = new DelayedSender(TimeSpan.FromSeconds(2), log);

        await using var factory = Start(a2a, sender, log, reconcile: true);
        var row = await SeedDispatchingAsync(factory.Services, taskId: Guid.NewGuid().ToString("N"));
        a2a.GetTaskHandler = (request, token) => request.Id == row.TaskId
            ? Task.FromResult(AgentTaskOf(row.TaskId!, "resposta da reconciliação", DateTimeOffset.UtcNow.AddMinutes(-1)))
            : FakeA2AClientFactory.TaskNotFoundHandler(request, token);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var stop = await StopAsync(factory, log);
        log.Print(output);

        Assert.True(stop < ComposeStopGracePeriod, $"parada levou {stop}");
        Assert.True(sender.Completed, "a entrega deveria ter terminado");
        Assert.Null(await FindDispatchAsync(row.SessionId));
    }

    [Fact]
    public async Task ReconciliationDeliveryInFlight_BeyondTheDeadline_IsCancelled_AndTheRowStaysClaimed()
    {
        var log = new Timeline();
        var a2a = new FakeA2AClientFactory();
        var sender = new DelayedSender(TimeSpan.FromSeconds(30), log);

        await using var factory = Start(a2a, sender, log, reconcile: true);
        var row = await SeedDispatchingAsync(factory.Services, taskId: Guid.NewGuid().ToString("N"));
        a2a.GetTaskHandler = (request, token) => request.Id == row.TaskId
            ? Task.FromResult(AgentTaskOf(row.TaskId!, "resposta lenta", DateTimeOffset.UtcNow.AddMinutes(-1)))
            : FakeA2AClientFactory.TaskNotFoundHandler(request, token);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var stop = await StopAsync(factory, log);
        log.Print(output);

        Assert.True(stop < ComposeStopGracePeriod, $"parada levou {stop}");
        Assert.True(sender.Cancelled, "a entrega deveria ter sido cancelada");
        var dispatch = await FindDispatchAsync(row.SessionId);
        Assert.NotNull(dispatch);
        Assert.Equal(PendingDispatchStatus.Dispatching, dispatch.Status);
        Assert.NotEqual(row.Token, dispatch.ExpectedToken);
        Assert.NotNull(dispatch.ReconciliationClaimedAt);
    }

    /// <summary>
    /// Medição da condição do portão 1: os três trabalhos em voo ao mesmo tempo
    /// (SendMessage no debounce, entrega na reconciliação, push no endpoint).
    /// </summary>
    [Fact]
    public async Task ThreeWorksInFlight_StopFitsTheGracePeriod()
    {
        var log = new Timeline();
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a2a = new FakeA2AClientFactory
        {
            BeforeReturn = async (_, _, token) =>
            {
                log.Mark("debounce: SendMessage começou (3 s)");
                sendEntered.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                log.Mark("debounce: SendMessage respondeu");
            },
        };
        var sender = new DelayedSender(TimeSpan.FromSeconds(4), log);

        await using var factory = Start(a2a, sender, log, reconcile: true);
        var client = factory.CreateClient();

        var reconciled = await SeedDispatchingAsync(factory.Services, taskId: Guid.NewGuid().ToString("N"));
        var pushed = await SeedDispatchingAsync(factory.Services, taskId: Guid.NewGuid().ToString("N"));
        a2a.GetTaskHandler = (request, _) => request.Id == reconciled.TaskId
            ? Task.FromResult(AgentTaskOf(reconciled.TaskId!, "reconciliada", DateTimeOffset.UtcNow.AddMinutes(-1)))
            : FakeA2AClientFactory.TaskNotFoundHandler(request, CancellationToken.None);
        await WaitUntilAsync(() => sender.EnteredCount >= 1, TimeSpan.FromSeconds(15));
        var push = PostPushAsync(client, AgentTaskOf(pushed.TaskId!, "pelo push"), pushed.Token);
        await WaitUntilAsync(() => sender.EnteredCount >= 2, TimeSpan.FromSeconds(15));
        await ReceiveAsync(factory.Services);
        await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var stop = await StopAsync(factory, log);
        await push;
        log.Print(output);

        Assert.True(stop < ComposeStopGracePeriod, $"parada levou {stop}");
        Assert.Null(await FindDispatchAsync(reconciled.SessionId));
        Assert.Null(await FindDispatchAsync(pushed.SessionId));
    }

    // ── Apoio ────────────────────────────────────────────────────────────────

    private InboxFactory Start(FakeA2AClientFactory a2a, DelayedSender sender, Timeline log, string window = "00:00:00.200", bool reconcile = false)
    {
        // Cada teste começa sem disparos: os que sobraram do teste anterior, na
        // mesma base, seriam reconciliados antes dos deste.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        using (var db = new AppDbContext(options))
        {
            db.PendingDispatches.ExecuteDelete();
        }

        var factory = new InboxFactory(fixture.Postgres.GetConnectionString(), a2a, sender, log, window, reconcile);
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    private static async Task<TimeSpan> StopAsync(InboxFactory factory, Timeline log)
    {
        log.Mark("=== parada pedida");
        var stopwatch = Stopwatch.StartNew();
        await factory.DisposeAsync();
        log.Mark($"=== parada concluída em {stopwatch.ElapsedMilliseconds} ms");
        return stopwatch.Elapsed;
    }

    private static Task PostPushAsync(HttpClient client, AgentTask task, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(task),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, token);
        return Task.Run(async () =>
        {
            try
            {
                await client.SendAsync(request);
            }
            catch (Exception)
            {
                // O cliente de teste é descartado com a fábrica; o servidor segue
                // processando com CancellationToken.None.
            }
        });
    }

    private sealed record SeededRow(Guid SessionId, string? TaskId, string Token);

    private static async Task<SeededRow> SeedDispatchingAsync(IServiceProvider services, string? taskId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", cipher.Encrypt("credencial"), Guid.NewGuid());
        db.Channels.Add(channel);
        var contact = new Contact(channel.Id, $"+5511{Guid.NewGuid():N}"[..15], new Dictionary<string, string>(), displayName: null);
        db.Contacts.Add(contact);
        var session = new Session(contact.Id);
        db.Sessions.Add(session);
        var dispatch = new PendingDispatch(session.Id, "oi", DateTimeOffset.UtcNow);
        var token = Guid.NewGuid().ToString("N");
        dispatch.MarkDispatching(token);
        if (taskId is not null)
        {
            dispatch.RegisterTaskId(taskId);
        }

        db.PendingDispatches.Add(dispatch);
        await db.SaveChangesAsync();
        return new SeededRow(session.Id, taskId, token);
    }

    private static async Task<Guid> ReceiveAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", cipher.Encrypt("credencial"), Guid.NewGuid());
        db.Channels.Add(channel);
        await db.SaveChangesAsync();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channel.Id, $"+5511{Guid.NewGuid():N}"[..15], "olá", MessageContentType.Text, Guid.NewGuid().ToString(),
            displayName: null, DateTimeOffset.UtcNow, new Dictionary<string, string>(), CancellationToken.None);
        var contact = await db.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channel.Id);
        return await db.Sessions.AsNoTracking().Where(s => s.ContactId == contact.Id).Select(s => s.Id).SingleAsync();
    }

    private async Task<PendingDispatch?> FindDispatchAsync(Guid sessionId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        return await db.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }

    private static AgentTask AgentTaskOf(string id, string text, DateTimeOffset? terminalAt = null) => new()
    {
        Id = id,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus { State = TaskState.Completed, Timestamp = terminalAt ?? DateTimeOffset.UtcNow },
        Artifacts = [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(text)] }],
    };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }

            await Task.Delay(50);
        }
    }

    private sealed class Timeline
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<(TimeSpan At, string What)> _events = new();

        public void Mark(string what) => _events.Enqueue((_clock.Elapsed, what));

        public void Print(ITestOutputHelper output)
        {
            var zero = _events.FirstOrDefault(e => e.What == "=== parada pedida").At;
            foreach (var (at, what) in _events.OrderBy(e => e.At))
            {
                output.WriteLine($"MEDIDA {(at - zero).TotalMilliseconds,8:F0} ms  {what}");
            }
        }
    }

    private sealed class DelayedSender(TimeSpan delay, Timeline log) : IOutboundMessageSender
    {
        private int _entered;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int EnteredCount => Volatile.Read(ref _entered);

        public bool Completed { get; private set; }

        public bool Cancelled { get; private set; }

        public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _entered);
            log.Mark($"entrega ao canal começou: {message.ResponseText} ({delay.TotalSeconds:F0} s)");
            Entered.TrySetResult();
            try
            {
                await Task.Delay(delay, cancellationToken);
                Completed = true;
                log.Mark($"entrega ao canal terminou: {message.ResponseText}");
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                log.Mark($"entrega ao canal CANCELADA: {message.ResponseText}");
                throw;
            }
        }
    }

    private sealed class InboxFactory(
        string connectionString,
        FakeA2AClientFactory a2a,
        DelayedSender sender,
        Timeline log,
        string window,
        bool reconcile) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                    ["Api:BaseUrl"] = "http://apps-api.test",
                    ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
                    ["Debounce:Window"] = window,
                    ["Debounce:SweepInterval"] = "00:00:00.050",
                    // Sem reconciliação no teste, o intervalo dela fica longe do
                    // que o teste mede; com ela, a task já está além da carência.
                    ["DispatchReconciliation:Interval"] = reconcile ? "00:00:00.100" : "01:00:00",
                    ["DispatchReconciliation:TerminalGrace"] = "00:00:01",
                });
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
                services.RemoveAll<IA2AClientFactory>();
                services.AddSingleton<IA2AClientFactory>(a2a);
                services.AddKeyedSingleton<IOutboundMessageSender>("test-channel", sender);

                // Ordem e duração da parada de cada IHostedService, na posição
                // em que o Program os registrou. O GenericWebHostService fica de
                // fora: embrulhá-lo sobe o servidor duas vezes.
                for (var i = 0; i < services.Count; i++)
                {
                    var descriptor = services[i];
                    if (descriptor.ServiceType != typeof(IHostedService) || descriptor.ImplementationType?.Name == "GenericWebHostService")
                    {
                        continue;
                    }

                    services[i] = ServiceDescriptor.Singleton<IHostedService>(sp => new TimedHostedService(Create(sp, descriptor), log));
                }
            });
        }

        private static IHostedService Create(IServiceProvider sp, ServiceDescriptor descriptor) =>
            descriptor.ImplementationInstance as IHostedService
            ?? (descriptor.ImplementationFactory is { } create
                ? (IHostedService)create(sp)
                : (IHostedService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
    }

    private sealed class TimedHostedService(IHostedService inner, Timeline log) : IHostedService
    {
        private int _stopped;

        public Task StartAsync(CancellationToken cancellationToken) => inner.StartAsync(cancellationToken);

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // A fábrica de teste chama StopAsync duas vezes (parada e descarte); a
            // segunda volta na hora e não é medida.
            var first = Interlocked.Exchange(ref _stopped, 1) == 0;
            if (first)
            {
                log.Mark($"StopAsync início {inner.GetType().Name}");
            }

            await inner.StopAsync(cancellationToken);
            if (first)
            {
                log.Mark($"StopAsync fim    {inner.GetType().Name}");
            }
        }
    }
}
