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
using InboxIssuerKeysRetriever = InboxAssembly::Buteco.Inbox.Auth.IssuerKeysRetriever;
using InboxAgentReferenceValidator = InboxAssembly::Buteco.Inbox.Agents.AgentReferenceValidator;
using InboxServiceAccessTokenSource = InboxAssembly::Buteco.Inbox.Auth.ServiceAccessTokenSource;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Npgsql;
using InboxProgram = InboxAssembly::Program;
using System.Security.Cryptography;
using System.Text;
using Buteco.Workers.Mcp;
using InboxOrchestratorRoundTrip.Tests.Support;

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
    private readonly PostgreSqlContainer _apiAndWorkersPostgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
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
    private IHost? _secondWorkersHost;

    // Resposta fixa do IChatClient mockado — o LLM de verdade não faz parte
    // do escopo desta fatia; só precisa provar que o texto de volta chega
    // intacto até a push notification recebida por apps/inbox.
    public const string MockedAgentReplyText = "Resposta do agente de teste do round-trip.";

    public const string OperatorPassword = "correct-horse-battery-staple";

    public WebApplicationFactory<ApiProgram> ApiFactory => _apiFactory!;

    public WebApplicationFactory<InboxProgram> InboxFactory => _inboxFactory!;

    // Controles das fontes de PendingDispatch órfã (#47). Neutros por padrão:
    // push passa direto, LLM responde — os testes que não os tocam continuam
    // exercitando o round-trip normal.
    public PushNotificationGate PushGate { get; } = new();

    public bool ChatClientFails { get; set; }

    // Controles da disputa pelo lock de contexto (#162, guarda 4 da D5 da change
    // inbox-serializacao-por-sessao). Neutros por padrão, como os de cima: LLM sem
    // atraso, Command Timeout default do Npgsql (30 s), uma instância de worker.
    public TimeSpan ChatClientDelay { get; set; } = TimeSpan.Zero;

    // Vale para os hosts de worker construídos DEPOIS de atribuído (segunda instância,
    // reinício) — o mesmo recurso de ConversationContextLockFailureTests: encurta a
    // espera pelo advisory lock produzindo a mesma exceção de produção.
    public int? WorkerCommandTimeoutSeconds { get; set; }

    // Emissor do apps/api ligado (validacao-jwks-inbox-connectors, D9): url pública fixa,
    // passada ao apps/inbox como Identity:Issuer SEM a barra final, como o compose faz com
    // PUBLIC_DOMAIN; o issuer do token sai com ela. O apps/inbox busca as chaves pelo
    // TestServer do apps/api, atrás deste portão: fechado, a busca falha como rede fora.
    public const string ApiPublicBaseUrl = "https://api.exemplo.test";

    public IssuerKeysGate ApiIssuerKeys { get; } = new();

    // Os segredos dos clientes de serviço do apps/api, com "+" (servicos-token-emissor, D9):
    // o achado da #234 é que o "+" mandado sem codificação chega como espaço. O do inbox é o
    // ServiceClient:Secret do apps/inbox desta fixture.
    public const string InboxServiceClientSecret = "f14iSytNZM8pkHkpeUKhBR8lVUmt+1IVX2bfd1aMqe8=";
    public const string ConnectorsServiceClientSecret = "6+/qzvxHoNNZ/UWxyy/McnQUWfKYdC5yf3Er/M1utmk=";

    /// <summary>
    /// Cada chamada do <c>apps/inbox</c> desta fixture ao <c>apps/api</c> real, pelos três
    /// clientes nomeados (A2A, validação de agente e token de serviço), com o status recebido
    /// e o token enviado (servicos-token-emissor, D9).
    /// </summary>
    public ConcurrentQueue<ServiceCall> InboxToApi { get; } = new();

    private readonly List<IAsyncDisposable> _extraInboxes = [];

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_apiAndWorkersPostgres.StartAsync(), _inboxPostgres.StartAsync(), _rabbitMq.StartAsync());

        _apiFactory = BuildApiFactory();

        using (var scope = _apiFactory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiAppDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        // O catálogo cadastra os clientes de serviço antes do primeiro pedido de token do
        // apps/inbox: sem eles, o pedido recebe invalid_client e abre a espera de 10 s
        // (servicos-token-emissor, D9).
        await WarmUpIssuerAsync();

        _inboxFactory = BuildInboxFactory(InboxServiceClientSecret, _inboxPostgres.GetConnectionString(), InboxToApi);

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
        await StopSecondWorkersAsync();

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

        foreach (var inbox in _extraInboxes)
        {
            await inbox.DisposeAsync();
        }

        await Task.WhenAll(_apiAndWorkersPostgres.DisposeAsync().AsTask(), _inboxPostgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    private WebApplicationFactory<ApiProgram> BuildApiFactory()
    {
        var factory = new WebApplicationFactory<ApiProgram>();

        return factory.WithWebHostBuilder(builder =>
        {
            // UseSetting: o emissor decide se liga no registro, antes do Build() (D8 da
            // change emissor-oidc-api); a cópia de baixo vale para quem lê depois.
            builder.UseSetting("PublicUrl:BaseUrl", ApiPublicBaseUrl);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PublicUrl:BaseUrl"] = ApiPublicBaseUrl,
                    ["Identity:SigningKeyEncryptionKey"] = "dGVzdC1pZGVudGl0eS1rZXktMzItYnl0ZXMtLS0tLS0=",
                    // Os segredos dos clientes de serviço, obrigatórios no boot do apps/api
                    // (clientes-servico-emissor, D2). Fixos aqui, e não herdados do
                    // appsettings.Development.json, como a chave acima.
                    ["Identity:ServiceClients:Inbox:Secret"] = InboxServiceClientSecret,
                    ["Identity:ServiceClients:Connectors:Secret"] = ConnectorsServiceClientSecret,
                    // Só precisa ser não-vazio — ProviderCatalogService.IsProviderConfigured("openai")
                    // olha só a presença da chave (libs/ProviderCatalog/LlmProviders.cs); a chamada
                    // real ao LLM nunca acontece, IChatClientResolver está mockado em apps/workers.
                    ["OpenAI:ApiKey"] = "test-api-key",
                    ["Auth:OperatorPasswordHash"] = ComputeOperatorPasswordHash(),
                    // O primeiro Admin do bootstrap, com o mesmo hash do operador
                    // (usuarios-locais-login-emissor, D2): é quem faz o fluxo real do
                    // IssuerTokenRoundTripTests.
                    ["Auth:BootstrapAdminEmail"] = IssuerUserFlow.BootstrapAdminEmail,
                    // Vazio por cima do Vite do appsettings.Development.json: o
                    // redirect_uri do buteco-painel sai da url pública.
                    ["Identity:PanelBaseUrl"] = "",
                }));

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApiAppDbContext>));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<ApiAppDbContext>(options => options.UseButecoAgentsNpgsql(_apiAndWorkersPostgres.GetConnectionString()));

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

    /// <summary>
    /// Um <c>apps/inbox</c> a mais, com outro <c>ServiceClient:Secret</c>, contra o mesmo
    /// <c>apps/api</c> e num banco próprio do mesmo contêiner, para as varreduras dele não
    /// disputarem as linhas do inbox principal. Os logs vão para <paramref name="logs"/>.
    /// Descartado no fim da fixture.
    /// </summary>
    public async Task<WebApplicationFactory<InboxProgram>> BuildExtraInboxAsync(
        string serviceClientSecret, ConcurrentQueue<ServiceCall> calls, ConcurrentQueue<(LogLevel Level, string Message)> logs)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_inboxPostgres.GetConnectionString())
        {
            Database = $"buteco_inbox_extra_{Guid.NewGuid():N}",
        }.ConnectionString;
        var inbox = BuildInboxFactory(serviceClientSecret, connectionString, calls, logs);
        _extraInboxes.Add(inbox);

        using var scope = inbox.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InboxAppDbContext>().Database.MigrateAsync();
        return inbox;
    }

    private WebApplicationFactory<InboxProgram> BuildInboxFactory(
        string serviceClientSecret,
        string connectionString,
        ConcurrentQueue<ServiceCall> calls,
        ConcurrentQueue<(LogLevel Level, string Message)>? logs = null)
    {
        var factory = new WebApplicationFactory<InboxProgram>();

        return factory.WithWebHostBuilder(builder =>
        {
            // UseSetting: o apps/inbox decide no registro se liga o esquema do emissor.
            builder.UseSetting("Identity:Issuer", ApiPublicBaseUrl);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:Issuer"] = ApiPublicBaseUrl,
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
                    // Reconciliação curta (#47): a carência de 1 s cobre com folga
                    // o push em processo, que aqui não atravessa rede.
                    ["DispatchReconciliation:Interval"] = "00:00:00.200",
                    ["DispatchReconciliation:TerminalGrace"] = "00:00:01",
                    // Longe de qualquer instante que os testes daqui fixam: o
                    // teste de messageInstant recebe uma mensagem com
                    // receivedAt de 10/03/2026, e o D7 mede a idade pelo
                    // LastMessageAt. Com 30 s, a reconciliação encerrava a linha
                    // recém-reivindicada como perda antes de o TaskId ser gravado
                    // (medido em 04/10/2026). O D7 tem guardas em apps/inbox.
                    ["DispatchReconciliation:UntrackedDispatchMaxAge"] = "3650.00:00:00",
                    // O segredo do cliente buteco-servico-inbox (servicos-token-emissor, D5).
                    ["ServiceClient:Secret"] = serviceClientSecret,
                }));

            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(new QueueLoggerProvider(logs)));
            }

            builder.ConfigureServices(services =>
            {
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InboxAppDbContext>));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<InboxAppDbContext>(options => options.UseNpgsql(connectionString));

                // Os três clientes do apps/inbox que chamam o apps/api vão ao TestServer
                // dele, sem rede real, passando pelo gravador: o A2A (SendMessage chega no
                // EnqueueingAgentHandler de verdade), a validação de agente do cadastro de
                // canal e o pedido de token de serviço ao emissor (servicos-token-emissor, D9).
                foreach (var name in new[] { InboxA2AClientFactory.HttpClientName, InboxAgentReferenceValidator.HttpClientName, InboxServiceAccessTokenSource.HttpClientName })
                {
                    services.AddHttpClient(name)
                        .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(name, calls) { InnerHandler = ApiFactory.Server.CreateHandler() });
                }

                // Chaves do emissor pelo TestServer do apps/api, o endereço interno do
                // teste (D3), atrás do portão.
                services.AddHttpClient(InboxIssuerKeysRetriever.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => ApiFactory.Server.CreateHandler())
                    .AddHttpMessageHandler(() => new IssuerKeysGateHandler(ApiIssuerKeys));
            });
        });
    }

    private IHost BuildWorkersHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = WorkerCommandTimeoutSeconds is { } commandTimeout
            ? $"{_apiAndWorkersPostgres.GetConnectionString()};Command Timeout={commandTimeout}"
            : _apiAndWorkersPostgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        // N = 1 FIXO em toda instância que a fixture constrói (subida, reinício,
        // segunda), e NÃO um controle como os de cima (change workers-consumo-concorrente,
        // D7). Não é neutro: é o regime de antes da #163, contra o default 4 de produção.
        // Dois testes dependem dele: a sentinela de OrphanDispatchRoundTripTests (só
        // fecha depois da mensagem devolvida PORQUE o consumo é serializado) e a perna
        // vermelha de SessionSerializationRoundTripTests (msg2 só cai na segunda
        // instância, a do Command Timeout curto, porque a primeira tem um slot só).
        // Gatilho para virar controle: o primeiro teste daqui que precise de N > 1.
        builder.Services.Configure<TaskConsumerOptions>(options => options.MaxConcurrentExecutions = 1);

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
            .Returns(async () =>
            {
                if (ChatClientDelay > TimeSpan.Zero)
                {
                    await Task.Delay(ChatClientDelay);
                }

                return ChatClientFails
                    ? throw new HttpRequestException("Falha simulada do provedor.")
                    : new ChatResponse(new ChatMessage(ChatRole.Assistant, MockedAgentReplyText));
            });

        var chatClientResolverMock = new Mock<IChatClientResolver>();
        chatClientResolverMock
            .Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(chatClientMock.Object);
        builder.Services.AddSingleton(chatClientResolverMock.Object);

        builder.Services.AddSingleton<Buteco.Workers.Mcp.IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<Buteco.Workers.AgentDelegations.IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<Buteco.Workers.Knowledge.Execution.IKnowledgeToolSetResolver, NullKnowledgeToolSetResolver>();

        // Redireciona o webhook de push notification de apps/workers para o
        // TestServer de apps/inbox, sem rede real — mesma técnica do
        // A2AClientFactory acima, na outra ponta do round-trip.
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => InboxFactory.Server.CreateHandler())
            .AddHttpMessageHandler(() => new PushNotificationGateHandler(PushGate));
        builder.Services.AddSingleton<PushNotificationSender>();

        // AgentExecutionService/TaskJobConsumer passaram a exigir
        // TimeProvider (change apps-workers-contexto-temporal).
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    /// <summary>
    /// Para o host de apps/workers e sobe outro, contra a mesma fila — o que um
    /// deploy faz. Mensagem devolvida à fila pela parada é reentregue ao novo.
    /// </summary>
    public async Task RestartWorkersAsync()
    {
        await _workersHost!.StopAsync();
        _workersHost.Dispose();
        _workersHost = BuildWorkersHost();
        await _workersHost.StartAsync();
    }

    /// <summary>
    /// Sobe uma segunda instância de apps/workers contra a mesma fila e o mesmo banco —
    /// o caso de várias instâncias, sem afirmar quantas o piloto roda (que muda e é
    /// registrado no 02). Parada por <see cref="StopSecondWorkersAsync"/>.
    /// </summary>
    public async Task StartSecondWorkersAsync()
    {
        _secondWorkersHost = BuildWorkersHost();
        await _secondWorkersHost.StartAsync();
    }

    public async Task StopSecondWorkersAsync()
    {
        if (_secondWorkersHost is null)
        {
            return;
        }

        await _secondWorkersHost.StopAsync();
        _secondWorkersHost.Dispose();
        _secondWorkersHost = null;
    }

    /// <summary>
    /// A conexão do banco de um <c>apps/api</c> da fixture: a chave do cadastro das 2 etapas do Admin
    /// do bootstrap no <see cref="IssuerUserFlow"/>, porque hosts sobre o mesmo banco compartilham o cadastro.
    /// </summary>
    public static string IssuerDatabaseOf(WebApplicationFactory<ApiProgram> api)
    {
        using var scope = api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApiAppDbContext>().Database.GetConnectionString()!;
    }

    // O access token de usuário do fluxo real contra apps/api (sessão,
    // authorize com PKCE e troca do código), do primeiro Admin do
    // bootstrap: o mesmo token com que o painel chama apps/api.
    public Task<string> UserAccessTokenAsync() =>
        IssuerUserFlow.AccessTokenAsync(
            IssuerDatabaseOf(ApiFactory),
            ApiFactory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            WarmUpIssuerAsync,
            ApiPublicBaseUrl,
            OperatorPassword,
            "openid buteco-api");

    // O access token de usuário do fluxo real com outro escopo, por exemplo o do
    // apps/inbox ("openid buteco-inbox"), para as rotas de canal.
    public Task<string> UserAccessTokenAsync(string scope) =>
        IssuerUserFlow.AccessTokenAsync(
            IssuerDatabaseOf(ApiFactory),
            ApiFactory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            WarmUpIssuerAsync,
            ApiPublicBaseUrl,
            OperatorPassword,
            scope);

    // O bootstrap e o catálogo pelos serviços do apps/api, idempotentes
    // (IssuerUserFlow.AccessTokenAsync, parâmetro warmUp).
    private async Task WarmUpIssuerAsync()
    {
        await using var scope = ApiFactory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Users.Bootstrap.IUserBootstrapper>().RunAsync(CancellationToken.None);
    }

    private static string ComputeOperatorPasswordHash()
    {
        const int iterations = 100_000;
        var salt = "fixed-test-salt-not-for-production"u8.ToArray();
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(OperatorPassword),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private sealed class RecordingHandler(string client, ConcurrentQueue<ServiceCall> calls) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            calls.Enqueue(new ServiceCall(client, request.Method.Method, request.RequestUri!.AbsolutePath, (int)response.StatusCode, request.Headers.Authorization?.Parameter));
            return response;
        }
    }

    private sealed class QueueLoggerProvider(ConcurrentQueue<(LogLevel Level, string Message)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new QueueLogger(logs);

        public void Dispose()
        {
        }

        private sealed class QueueLogger(ConcurrentQueue<(LogLevel Level, string Message)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                logs.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Uma chamada de um app de teste a outro, pelo cliente nomeado.</summary>
public sealed record ServiceCall(string Client, string Method, string Path, int Status, string? Token);
