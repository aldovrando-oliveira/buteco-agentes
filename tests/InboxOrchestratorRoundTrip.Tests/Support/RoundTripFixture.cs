extern alias ApiAssembly;
extern alias InboxAssembly;

using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using ApiAppDbContext = ApiAssembly::Buteco.Api.Infrastructure.AppDbContext;
using ApiRabbitMqOptions = ApiAssembly::Buteco.Api.Options.RabbitMqOptions;
using ApiProgram = ApiAssembly::Program;
using InboxAppDbContext = InboxAssembly::Buteco.Inbox.Infrastructure.AppDbContext;
using InboxA2AClientFactory = InboxAssembly::Buteco.Inbox.Orchestration.A2AClientFactory;
using InboxProgram = InboxAssembly::Program;

namespace InboxOrchestratorRoundTrip.Tests.Support;

/// <summary>
/// Monta os três apps reais desta change (design.md, Decisão 10): apps/api
/// e apps/inbox como <see cref="WebApplicationFactory{TEntryPoint}"/> reais
/// (cada um com seu próprio Postgres — apps/api e apps/workers
/// compartilham um Postgres, apps/inbox tem o dele, mesma separação de
/// bancos da configuração real do projeto), apps/workers como um
/// <see cref="IHost"/> mínimo (mesmo padrão de
/// <c>AgentDelegationConcurrencyTests</c>, apps/workers) consumindo a
/// mesma fila RabbitMQ publicada por apps/api.
///
/// As duas pontas HTTP entre os três processos não usam rede real: o
/// HttpClient nomeado de apps/inbox para o cliente A2A é redirecionado
/// para o <see cref="TestServer"/> de apps/api
/// (<see cref="WebApplicationFactory{TEntryPoint}.Server"/>), e o HttpClient
/// nomeado de apps/workers para o webhook de push notification é
/// redirecionado para o <see cref="TestServer"/> de apps/inbox — mesmo
/// mecanismo já usado neste projeto para <c>AgentReferenceValidator</c>
/// (<c>FakeAgentApiHttpMessageHandler</c>), só que apontando para um app
/// de teste real em vez de um fake.
/// </summary>
public sealed class RoundTripFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _apiAndWorkersPostgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_agents_roundtrip_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private readonly PostgreSqlContainer _inboxPostgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("buteco_inbox_roundtrip_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.3-management")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private WebApplicationFactory<ApiProgram>? _apiFactory;
    private WebApplicationFactory<InboxProgram>? _inboxFactory;
    private IHost? _workersHost;

    // Resposta fixa do IChatClient mockado — o LLM de verdade não faz parte
    // do escopo desta fatia; só precisa provar que o texto de volta chega
    // intacto até a push notification recebida por apps/inbox.
    public const string MockedAgentReplyText = "Resposta do agente de teste do round-trip.";

    public WebApplicationFactory<ApiProgram> ApiFactory => _apiFactory!;

    public WebApplicationFactory<InboxProgram> InboxFactory => _inboxFactory!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_apiAndWorkersPostgres.StartAsync(), _inboxPostgres.StartAsync(), _rabbitMq.StartAsync());

        _apiFactory = BuildApiFactory();

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiAppDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        _inboxFactory = BuildInboxFactory();

        using (var scope = _inboxFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        _workersHost = BuildWorkersHost();
        await _workersHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_workersHost is not null)
        {
            await _workersHost.StopAsync();
            _workersHost.Dispose();
        }

        if (_apiFactory is not null)
        {
            await _apiFactory.DisposeAsync();
        }

        if (_inboxFactory is not null)
        {
            await _inboxFactory.DisposeAsync();
        }

        await Task.WhenAll(_apiAndWorkersPostgres.DisposeAsync().AsTask(), _inboxPostgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    private WebApplicationFactory<ApiProgram> BuildApiFactory()
    {
        var factory = new WebApplicationFactory<ApiProgram>();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Só precisa ser não-vazio — ProviderCatalogService.IsProviderConfigured("openai")
                    // olha só a presença da chave (libs/ProviderCatalog/LlmProviders.cs); a chamada
                    // real ao LLM nunca acontece, IChatClientResolver está mockado em apps/workers.
                    ["OpenAI:ApiKey"] = "test-api-key",
                }));

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApiAppDbContext>));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<ApiAppDbContext>(options => options.UseNpgsql(_apiAndWorkersPostgres.GetConnectionString()));

                services.Configure<ApiRabbitMqOptions>(options =>
                {
                    options.Host = _rabbitMq.Hostname;
                    options.Port = _rabbitMq.GetMappedPublicPort(5672);
                    options.Username = "buteco";
                    options.Password = "buteco_test_password";
                });
            });
        });
    }

    private WebApplicationFactory<InboxProgram> BuildInboxFactory()
    {
        var factory = new WebApplicationFactory<InboxProgram>();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                    // Placeholders — nenhuma rede real é usada para alcançar
                    // apps/api (ver ConfigurePrimaryHttpMessageHandler
                    // abaixo); a URL só precisa existir para
                    // Uri/HttpRequestMessage não rejeitarem a montagem.
                    ["Api:BaseUrl"] = "http://apps-api.test",
                    ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
                    ["Debounce:Window"] = "00:00:00.300",
                    ["Debounce:SweepInterval"] = "00:00:00.050",
                    ["Debounce:MaxDispatchAttempts"] = "3",
                }));

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InboxAppDbContext>));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<InboxAppDbContext>(options => options.UseNpgsql(_inboxPostgres.GetConnectionString()));

                // Redireciona o cliente A2A de apps/inbox para o TestServer
                // de apps/api, sem rede real — SendMessage chega no
                // EnqueueingAgentHandler de verdade.
                services.AddHttpClient(InboxA2AClientFactory.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => ApiFactory.Server.CreateHandler());
            });
        });
    }

    private IHost BuildWorkersHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = _apiAndWorkersPostgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = _rabbitMq.Hostname;
            options.Port = _rabbitMq.GetMappedPublicPort(5672);
            options.Username = "buteco";
            options.Password = "buteco_test_password";
        });

        var chatClientMock = new Mock<IChatClient>();
        chatClientMock
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, MockedAgentReplyText)));

        var chatClientResolverMock = new Mock<IChatClientResolver>();
        chatClientResolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(chatClientMock.Object);
        builder.Services.AddSingleton(chatClientResolverMock.Object);

        builder.Services.AddSingleton<Buteco.Workers.Mcp.IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<Buteco.Workers.AgentDelegations.IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();

        // Redireciona o webhook de push notification de apps/workers para o
        // TestServer de apps/inbox, sem rede real — mesma técnica do
        // A2AClientFactory acima, na outra ponta do round-trip.
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => InboxFactory.Server.CreateHandler());
        builder.Services.AddSingleton<PushNotificationSender>();

        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }
}
