extern alias ApiAssembly;
extern alias ConnectorsAssembly;

using System.Net.Http.Headers;
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
using ApiTokenService = ApiAssembly::Buteco.Api.Auth.ITokenService;
using ConnectorsProgram = ConnectorsAssembly::Program;
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
    // Mesmo valor literal das fixtures de apps/api, apps/inbox e apps/connectors; sem
    // código compartilhado entre os projetos de teste.
    public const string TokenSigningKey = "test-signing-key-shared-between-api-and-inbox-fixtures";

    private const string ConnectorsBaseUrl = "http://connectors.roundtrip.test";

    private const string ApiBaseUrl = "http://api.roundtrip.test";

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

    /// <summary>O que o <c>apps/api</c> publicou para indexação (1.2, #138).</summary>
    public InMemoryIndexingPublisher Indexing { get; } = new();

    public async Task InitializeAsync()
    {
        await _apiPostgres.StartAsync();

        var (api, _) = Build(TokenSigningKey);
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApiAppDbContext>().Database.MigrateAsync();
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
    /// Os dois hosts ligados. <paramref name="connectorsSigningKey"/> diferente da chave
    /// do <c>apps/api</c> é o caso da chave divergente.
    /// </summary>
    public (WebApplicationFactory<ApiProgram> Api, WebApplicationFactory<ConnectorsProgram> Connectors) Build(string connectorsSigningKey)
    {
        // Os dois sentidos: o apps/api chama o apps/connectors (validação de pasta, #104) e
        // o apps/connectors chama o apps/api (ciclo de sincronização, #105). Cada handler
        // primário é o TestServer do outro, resolvido na primeira chamada.
        WebApplicationFactory<ApiProgram>? api = null;
        var connectors = new WebApplicationFactory<ConnectorsProgram>().WithWebHostBuilder(builder =>
        {
            // UseSetting, e não ConfigureAppConfiguration: o Program.cs do apps/connectors
            // lê a chave e a credencial do Google ANTES do Build() (medido na #103,
            // ConnectorsFactory). Credencial do Google vazia: o provedor google-drive não
            // existe neste host.
            builder.UseSetting("Auth:TokenSigningKey", connectorsSigningKey);
            builder.UseSetting("GoogleDrive:ServiceAccountKeyBase64", string.Empty);
            // O apps/api desta fixture, e não o localhost:5017 do appsettings.Development.json
            // do apps/connectors (design.md da change ciclo-de-sincronizacao, D2).
            builder.UseSetting("Api:BaseUrl", ApiBaseUrl);
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
            });
        });

        api = new WebApplicationFactory<ApiProgram>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:TokenSigningKey"] = TokenSigningKey,
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
        _disposables.Add(connectors);
        return (api, connectors);
    }

    /// <summary>Cliente do <c>apps/api</c> com token de operador, sem login.</summary>
    public static HttpClient OperatorClient(WebApplicationFactory<ApiProgram> api)
    {
        var client = api.CreateClient();
        var (token, _) = api.Services.GetRequiredService<ApiTokenService>().Issue("operator", TimeSpan.FromMinutes(30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
