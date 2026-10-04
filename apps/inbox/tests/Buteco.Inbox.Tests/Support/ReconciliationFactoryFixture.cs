using System.Collections.Concurrent;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// apps/inbox real com a reconciliação de Dispatching em valores curtos (#47):
/// intervalo de 100 ms, carência de 3 s, limite de 4 s para linha sem TaskId.
/// O debounce roda junto (janela de 300 ms), contra o
/// <see cref="FakeA2AClientFactory"/>, que também responde o GetTask.
/// </summary>
public sealed class ReconciliationFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string GatedChannelType = "test-channel-gated";

    public static readonly TimeSpan TerminalGrace = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan UntrackedDispatchMaxAge = TimeSpan.FromSeconds(4);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_inbox_reconciliation_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public FakeA2AClientFactory A2AClientFactory { get; } = new();

    public GatedOutboundMessageSender GatedSender { get; } = new();

    public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Logs { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

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
                ["DispatchReconciliation:Interval"] = "00:00:00.100",
                ["DispatchReconciliation:TerminalGrace"] = TerminalGrace.ToString(),
                ["DispatchReconciliation:UntrackedDispatchMaxAge"] = UntrackedDispatchMaxAge.ToString(),
            });
            config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
        });

        builder.ConfigureLogging(logging => logging.AddProvider(new QueueLoggerProvider(Logs)));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));

            services.RemoveAll<IA2AClientFactory>();
            services.AddSingleton<IA2AClientFactory>(A2AClientFactory);

            services.AddKeyedSingleton<IOutboundMessageSender>(GatedChannelType, GatedSender);
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

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private sealed class QueueLoggerProvider(ConcurrentQueue<(LogLevel, string, string)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Buteco.Inbox", StringComparison.Ordinal)
                ? new QueueLogger(logs, categoryName)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }
    }

    private sealed class QueueLogger(ConcurrentQueue<(LogLevel, string, string)> logs, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            logs.Enqueue((logLevel, category, formatter(state, exception)));
    }
}

/// <summary>
/// Sender de canal que registra cada entrega e pode segurá-la num portão até o
/// teste soltar — o intervalo entre a reivindicação e a remoção da linha.
/// </summary>
public sealed class GatedOutboundMessageSender : IOutboundMessageSender
{
    private TaskCompletionSource _gate = Open();

    public ConcurrentQueue<OutboundMessage> Delivered { get; } = new();

    public Exception? ExceptionToThrow { get; set; }

    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Close()
    {
        _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release() => _gate.TrySetResult();

    public async Task SendAsync(OutboundMessage message, CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await _gate.Task.WaitAsync(cancellationToken);
        if (ExceptionToThrow is { } exception)
        {
            throw exception;
        }

        Delivered.Enqueue(message);
    }

    private static TaskCompletionSource Open()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
