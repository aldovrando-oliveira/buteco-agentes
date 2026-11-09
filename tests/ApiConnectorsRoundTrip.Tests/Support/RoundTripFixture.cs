extern alias ApiAssembly;
extern alias ConnectorsAssembly;

using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using ApiAppDbContext = ApiAssembly::Buteco.Api.Infrastructure.AppDbContext;
using ApiConnectorsFolderClient = ApiAssembly::Buteco.Api.KnowledgeSync.Connectors.ConnectorsFolderClient;
using ApiProgram = ApiAssembly::Program;
using ApiIndexingPublisher = ApiAssembly::Buteco.Api.Knowledge.Indexing.IKnowledgeIndexingJobPublisher;
using ConnectorsProgram = ConnectorsAssembly::Program;
using ConnectorsIssuerKeysRetriever = ConnectorsAssembly::Buteco.Connectors.Auth.IssuerKeysRetriever;
using ConnectorsIssuerKeysState = ConnectorsAssembly::Buteco.Connectors.Auth.IssuerKeysState;
using ConnectorsServiceAccessTokenSource = ConnectorsAssembly::Buteco.Connectors.Auth.ServiceAccessTokenSource;
using ConnectorsSyncApiClient = ConnectorsAssembly::Buteco.Connectors.Sync.SyncApiClient;
using ConnectorsSyncScheduler = ConnectorsAssembly::Buteco.Connectors.Sync.SyncSchedulerService;

namespace ApiConnectorsRoundTrip.Tests.Support;

/// <summary>
/// Os dois apps reais (design.md da change criacao-base-sincronizada, D8): o
/// <c>apps/api</c> com o próprio Postgres, e o <c>apps/connectors</c>, sem banco, com o
/// conector falso dele na chave <see cref="FakeConnector.Key"/> e sem credencial do
/// Google. Fonte de contêiner nova autorizada pelo mantenedor em 03/10/2026.
///
/// <para>
/// A chamada entre os dois não usa rede: o handler primário do <c>HttpClient</c>
/// nomeado do <c>apps/api</c> é o <c>TestServer</c> do <c>apps/connectors</c>, como o
/// <c>InboxOrchestratorRoundTrip</c> faz entre <c>apps/inbox</c> e <c>apps/api</c>.
/// Passam pelo caminho real o <c>ServiceTokenDelegatingHandler</c> do <c>apps/api</c>,
/// a autenticação e a tabela de subjects do <c>apps/connectors</c>, a rota de
/// descrição e o formato de fio dos dois lados (convenção 11).
/// </para>
/// </summary>
public sealed class RoundTripFixture : IAsyncLifetime
{
    private const string ConnectorsBaseUrl = "http://connectors.roundtrip.test";

    private const string ApiBaseUrl = "http://api.roundtrip.test";

    // Emissor do apps/api ligado (validacao-jwks-inbox-connectors, D9): url pública fixa,
    // passada ao apps/connectors como Identity:Issuer SEM a barra final, como o compose faz
    // com PUBLIC_DOMAIN. O apps/connectors busca as chaves pelo TestServer do apps/api
    // (o endereço interno do teste), atrás deste portão: fechado, a busca falha como rede
    // fora.
    public const string ApiPublicBaseUrl = "https://api.exemplo.test";

    /// <summary>A senha do primeiro Admin do bootstrap.</summary>
    public const string AdminPassword = "senha-do-admin-do-acordo";

    // Os segredos dos clientes de serviço do apps/api, com "+" (servicos-token-emissor, D9):
    // o achado da #234 é que o "+" mandado sem codificação chega como espaço. O do connectors
    // é o ServiceClient:Secret do apps/connectors desta fixture, salvo no caso divergente.
    public const string InboxServiceClientSecret = "f14iSytNZM8pkHkpeUKhBR8lVUmt+1IVX2bfd1aMqe8=";
    public const string ConnectorsServiceClientSecret = "6+/qzvxHoNNZ/UWxyy/McnQUWfKYdC5yf3Er/M1utmk=";

    public IssuerKeysGate ApiIssuerKeys { get; } = new();

    private readonly PostgreSqlContainer _apiPostgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_connectors_roundtrip_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private readonly List<IAsyncDisposable> _disposables = [];

    public FakeConnector Fake { get; } = new();

    /// <summary>
    /// Cada requisição do <c>apps/connectors</c> ao <c>apps/api</c>, como método e caminho,
    /// com o status recebido e o token enviado: é como a ida e volta conta os upserts que
    /// chegaram (9.4), e afirma que nenhuma recebeu <c>401</c>/<c>403</c> e que o token é
    /// <c>service:connectors</c> para o próprio <c>apps/api</c> (9.3).
    /// </summary>
    public System.Collections.Concurrent.ConcurrentQueue<(string Method, string Path, int Status, string? Token)> ConnectorsToApi { get; } = new();

    /// <summary>
    /// Os pedidos de token de serviço do <c>apps/connectors</c> ao emissor do <c>apps/api</c>,
    /// à parte das chamadas do ciclo, para as contagens de upsert não mudarem
    /// (servicos-token-emissor, D9).
    /// </summary>
    public System.Collections.Concurrent.ConcurrentQueue<(string Method, string Path, int Status, string? Token)> ConnectorsTokenRequests { get; } = new();

    /// <summary>O que o <c>apps/api</c> publicou para indexação (1.2, #138).</summary>
    public InMemoryIndexingPublisher Indexing { get; } = new();

    public async Task InitializeAsync()
    {
        await _apiPostgres.StartAsync();

        var (api, _) = Build();
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAppDbContext>().Database.MigrateAsync();

        // O catálogo cadastra os clientes de serviço antes do primeiro pedido de token do
        // apps/connectors: sem eles, o pedido recebe invalid_client e abre a espera de 10 s
        // (servicos-token-emissor, D9). Os hosts seguintes usam o mesmo banco.
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }

        await _apiPostgres.DisposeAsync();
    }

    /// <summary>
    /// Os dois hosts ligados. <paramref name="connectorsServiceClientSecret"/> diferente do
    /// segredo do catálogo é o caso do segredo divergente (S3). Com
    /// <paramref name="connectorsKeysFrom"/>, o <c>apps/connectors</c> obtém as chaves desse
    /// emissor de teste, que anuncia o mesmo issuer do <c>apps/api</c> com outras chaves: é o
    /// caso das chaves divergentes (S4) (servicos-token-emissor, D9; decisão do dono,
    /// 03/11/2026, na parada da 5.7).
    /// </summary>
    public (WebApplicationFactory<ApiProgram> Api, WebApplicationFactory<ConnectorsProgram> Connectors) Build(
        string connectorsServiceClientSecret = ConnectorsServiceClientSecret,
        TestIssuer? connectorsKeysFrom = null)
    {
        // Os dois sentidos: o apps/api chama o apps/connectors (validação de pasta, #104) e
        // o apps/connectors chama o apps/api (ciclo de sincronização, #105). Cada handler
        // primário é o TestServer do outro, resolvido na primeira chamada.
        WebApplicationFactory<ApiProgram>? api = null;
        var connectors = new WebApplicationFactory<ConnectorsProgram>().WithWebHostBuilder(builder =>
        {
            // UseSetting, e não ConfigureAppConfiguration: o Program.cs do apps/connectors
            // lê a credencial do Google ANTES do Build() (medido na #103, ConnectorsFactory).
            // Credencial do Google vazia: o provedor google-drive não existe neste host.
            builder.UseSetting("GoogleDrive:ServiceAccountKeyBase64", string.Empty);
            // O apps/api desta fixture, e não o localhost:5017 do appsettings.Development.json
            // do apps/connectors (design.md da change ciclo-de-sincronizacao, D2).
            builder.UseSetting("Api:BaseUrl", ApiBaseUrl);
            builder.UseSetting("Identity:Issuer", ApiPublicBaseUrl);
            // O segredo do cliente buteco-servico-connectors (servicos-token-emissor, D5).
            builder.UseSetting("ServiceClient:Secret", connectorsServiceClientSecret);
            builder.ConfigureServices(services =>
            {
                services.AddConnector(FakeConnector.Key, FakeConnector.AccountEmail, _ => Fake, _ => Fake);

                // O ciclo roda só quando o teste pede, pelo "Sincronizar agora" (D11).
                var scheduler = services.SingleOrDefault(descriptor => descriptor.ImplementationType == typeof(ConnectorsSyncScheduler));
                if (scheduler is not null)
                {
                    services.Remove(scheduler);
                }

                services.AddHttpClient(ConnectorsSyncApiClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(ConnectorsToApi) { InnerHandler = api!.Server.CreateHandler() });

                // O pedido de token de serviço vai ao emissor do apps/api real.
                services.AddHttpClient(ConnectorsServiceAccessTokenSource.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(ConnectorsTokenRequests) { InnerHandler = api!.Server.CreateHandler() });

                services.AddHttpClient(ConnectorsIssuerKeysRetriever.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => connectorsKeysFrom?.CreateHandler() ?? api!.Server.CreateHandler())
                    .AddHttpMessageHandler(() => new IssuerKeysGateHandler(ApiIssuerKeys));
            });
        });

        api = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
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
                    // appsettings.Development.json.
                    ["Identity:ServiceClients:Inbox:Secret"] = InboxServiceClientSecret,
                    ["Identity:ServiceClients:Connectors:Secret"] = ConnectorsServiceClientSecret,
                    // O primeiro Admin do bootstrap (usuarios-locais-login-emissor, D2): é
                    // quem faz o fluxo real do IssuerTokenRoundTripTests.
                    ["Auth:BootstrapAdminEmail"] = IssuerUserFlow.BootstrapAdminEmail,
                    // Vazio por cima do Vite do appsettings.Development.json: o
                    // redirect_uri do buteco-painel sai da url pública.
                    ["Identity:PanelBaseUrl"] = "",
                    ["Auth:OperatorPasswordHash"] = IssuerUserFlow.LegacyPasswordHash(AdminPassword),
                    ["Connectors:BaseUrl"] = ConnectorsBaseUrl,
                    // A checagem de fuso do apps/api compara com o fuso da máquina.
                    ["TZ"] = TimeZoneInfo.Local.Id,
                }));

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApiAppDbContext>));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApiAppDbContext>(options =>
                    options.UseNpgsql(_apiPostgres.GetConnectionString(), npgsql => npgsql.UseVector()));

                services.AddHttpClient(ApiConnectorsFolderClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => connectors.Server.CreateHandler());

                services.RemoveAll<ApiIndexingPublisher>();
                services.AddSingleton<ApiIndexingPublisher>(Indexing);
            });
        });

        _disposables.Add(api);
        ApiOf.AddOrUpdate(connectors, api);
        _disposables.Add(connectors);
        return (api, connectors);
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

    /// <summary>
    /// Cliente do <c>apps/api</c> com o access token do primeiro Admin, pelo fluxo real
    /// (sessão, authorize com PKCE e troca do código), como o painel chama o <c>apps/api</c>
    /// (fixtures-token-emissor, D4). Um login por host: cada <see cref="Build"/> sobe um
    /// <c>apps/api</c> novo.
    /// </summary>
    public static async Task<HttpClient> OperatorClientAsync(WebApplicationFactory<ApiProgram> api)
    {
        var token = await UserAccessTokens.GetValue(api, host => IssuerUserFlow.AccessTokenAsync(
            IssuerDatabaseOf(host),
            host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            () => WarmUpIssuerAsync(host),
            ApiPublicBaseUrl,
            AdminPassword,
            "openid buteco-api"));
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Um cliente do <c>apps/api</c> com o token do <c>buteco-servico-connectors</c>, por
    /// <c>client_credentials</c> no <c>/connect/token</c> real: quem chama as rotas de
    /// <c>/sync/knowledge-bases</c> em produção. O token do painel recebe <c>403</c> nelas desde a change
    /// papeis-no-token (Q8f).
    /// </summary>
    public static async Task<HttpClient> ConnectorsServiceClientAsync(WebApplicationFactory<ApiProgram> api)
    {
        await WarmUpIssuerAsync(api);
        var response = await api.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "buteco-servico-connectors",
            ["client_secret"] = ConnectorsServiceClientSecret,
            ["scope"] = "buteco-api",
        }));
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Token do buteco-servico-connectors falhou: {(int)response.StatusCode} {body}");
        }

        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private static readonly ConditionalWeakTable<WebApplicationFactory<ApiProgram>, Task<string>> UserAccessTokens = new();

    private static readonly ConditionalWeakTable<WebApplicationFactory<ApiProgram>, Task<string>> ConnectorsUserAccessTokens = new();

    private static readonly ConditionalWeakTable<WebApplicationFactory<ConnectorsProgram>, WebApplicationFactory<ApiProgram>> ApiOf = new();

    /// <summary>
    /// Cliente do <c>apps/connectors</c> com o access token do primeiro Admin para o escopo
    /// <c>buteco-connectors</c>, emitido pelo <c>apps/api</c> do mesmo <see cref="Build"/>
    /// (fixtures-token-emissor, D4). Espera o <c>apps/connectors</c> obter as chaves do emissor:
    /// antes disso, ele responde <c>503</c> a toda rota autenticada.
    /// </summary>
    public static async Task<HttpClient> ConnectorsOperatorClientAsync(WebApplicationFactory<ConnectorsProgram> connectors)
    {
        Assert.True(ApiOf.TryGetValue(connectors, out var api), "o apps/connectors não saiu de um Build desta fixture");
        var token = await ConnectorsUserAccessTokens.GetValue(api, host => IssuerUserFlow.AccessTokenAsync(
            IssuerDatabaseOf(host),
            host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            () => WarmUpIssuerAsync(host),
            ApiPublicBaseUrl,
            AdminPassword,
            "openid buteco-connectors"));

        var keys = connectors.Services.GetRequiredService<ConnectorsIssuerKeysState>();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!keys.Obtained)
        {
            Assert.True(DateTime.UtcNow < deadline, "o apps/connectors não obteve as chaves do emissor em 30 s");
            await Task.Delay(25);
        }

        var client = connectors.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>O catálogo de clientes e o bootstrap do primeiro Admin, idempotentes.</summary>
    public static async Task WarmUpIssuerAsync(WebApplicationFactory<ApiProgram> api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Users.Bootstrap.IUserBootstrapper>().RunAsync(CancellationToken.None);
    }

    public static async Task<List<(string? Name, string? Url)>> StoredFolderAsync(
        WebApplicationFactory<ApiProgram> api, string folderId)
    {
        using var scope = api.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiAppDbContext>();
        var rows = await dbContext.KnowledgeBases.AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.SyncFolderId == folderId)
            .Select(knowledgeBase => new { knowledgeBase.SyncFolderName, knowledgeBase.SyncFolderUrl })
            .ToListAsync();
        return rows.Select(row => (row.SyncFolderName, row.SyncFolderUrl)).ToList();
    }

    private sealed class RecordingHandler(System.Collections.Concurrent.ConcurrentQueue<(string Method, string Path, int Status, string? Token)> requests) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            requests.Enqueue((request.Method.Method, request.RequestUri!.AbsolutePath, (int)response.StatusCode, request.Headers.Authorization?.Parameter));
            return response;
        }
    }
}
