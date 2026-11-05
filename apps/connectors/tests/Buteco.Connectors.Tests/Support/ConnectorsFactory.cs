using System.Net.Http.Headers;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Buteco.Connectors.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// Host de teste. Sem rede: os dois <c>HttpClient</c> nomeados do Google têm o
/// handler primário trocado por <see cref="Google"/>. O conector falso entra só aqui
/// (chave <see cref="FakeConnector.Key"/>), nunca no Program.cs.
/// <para>
/// A validação do token do emissor fica sempre ligada, contra o <see cref="TestIssuer"/>
/// recebido em <paramref name="issuer"/> ou um próprio (<see cref="Issuer"/>), porque o app
/// não sobe sem <c>Identity:Issuer</c> (change remover-hmac-token-servico, D2 e D6):
/// <c>Identity:Issuer</c> e <c>Api:BaseUrl</c> dele, o cliente <c>IssuerKeys</c> com o handler
/// dele e o <paramref name="issuerKeysSchedule"/>. O <c>extraConfiguration</c> sobrescreve
/// os dois.
/// </para>
/// <para>
/// O <c>Api:BaseUrl</c> liga também o ciclo de sincronização, contra o <see cref="SyncApi"/>.
/// Quando o teste não passou <paramref name="issuer"/> nem pediu o <c>Api:BaseUrl</c>, o
/// agendador sai do DI da fábrica, para não somar pedidos aos duplos. É ajuste de teste, e
/// não opção de produção (fixtures-token-emissor, D3, decisão do dono de 04/11/2026).
/// </para>
/// </summary>
public sealed class ConnectorsFactory(
    bool withFakeConnector = true,
    string? googleKeyBase64 = null,
    IReadOnlyDictionary<string, string?>? extraConfiguration = null,
    string environment = "Development",
    Action<IServiceCollection>? configureServices = null,
    TestIssuer? issuer = null,
    IssuerKeysSchedule? issuerKeysSchedule = null) : WebApplicationFactory<Program>
{
    /// <summary>
    /// O apps/api falso, ligado ao cliente do ciclo de sincronização. Só é chamado quando o
    /// teste configura Api:BaseUrl.
    /// </summary>
    public FakeSyncApiHandler SyncApi { get; } = new();

    /// <summary>
    /// O <c>/connect/token</c> do emissor, que o handler de saída chama antes do
    /// <see cref="SyncApi"/> (servicos-token-emissor, D6). Sempre responde um token.
    /// </summary>
    public FakeTokenEndpoint TokenEndpoint { get; } = new();

    public GoogleDriveFakeHandler Google { get; } = new();

    public FakeConnector Fake { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>O emissor de teste do host: o recebido, ou um próprio.</summary>
    public TestIssuer Issuer { get; } = issuer ?? new TestIssuer();

    /// <summary>
    /// A <see cref="IServiceCollection"/> real, capturada pelo ConfigureServices, que
    /// roda depois de todos os registros do Program.cs (convenção 8).
    /// </summary>
    public IServiceCollection? CapturedServices { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, e não ConfigureAppConfiguration: o Program.cs lê o CORS e a credencial
        // do Google ANTES do Build(), e só o que entra por UseSetting já está na
        // configuração nesse ponto (medido: com ConfigureAppConfiguration, o boot subia com
        // os valores do appsettings.Development.json).
        builder.UseEnvironment(environment);
        var values = new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://painel.test",
            // O emissor de teste, e não o localhost:5017 do appsettings.Development.json:
            // sem isto, cada teste buscaria as chaves e ligaria o ciclo contra ele.
            ["Identity:Issuer"] = TestIssuer.Issuer,
            ["Api:BaseUrl"] = TestIssuer.InternalBaseUrl,
            // Obrigatório no boot com Api:BaseUrl (servicos-token-emissor, D5).
            ["ServiceClient:Secret"] = TestAuthentication.ServiceClientSecret,
        };

        var withoutScheduler = issuer is null && extraConfiguration?.ContainsKey("Api:BaseUrl") != true;

        if (googleKeyBase64 is not null)
        {
            values["GoogleDrive:ServiceAccountKeyBase64"] = googleKeyBase64;
        }

        foreach (var (key, value) in extraConfiguration ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        foreach (var (key, value) in values.Where(pair => pair.Value is not null))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));

        builder.ConfigureServices(services =>
        {
            CapturedServices = services;

            if (withFakeConnector)
            {
                services.AddConnector(
                    FakeConnector.Key,
                    FakeConnector.AccountEmail,
                    _ => Fake,
                    _ => Fake);
            }

            services.AddHttpClient(GoogleDriveHttp.DriveClientName).ConfigurePrimaryHttpMessageHandler(() => Google);
            services.AddHttpClient(GoogleDriveHttp.OAuthClientName).ConfigurePrimaryHttpMessageHandler(() => Google);
            services.AddHttpClient(Buteco.Connectors.Sync.SyncApiClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => SyncApi);
            services.AddHttpClient(ServiceAccessTokenSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => TokenEndpoint);

            services.Replace(ServiceDescriptor.Singleton(issuerKeysSchedule ?? new IssuerKeysSchedule()));
            services.AddHttpClient(IssuerKeysRetriever.HttpClientName).ConfigurePrimaryHttpMessageHandler(Issuer.CreateHandler);

            if (withoutScheduler)
            {
                var scheduler = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(SyncSchedulerService));
                services.Remove(scheduler);
            }

            configureServices?.Invoke(services);
        });
    }

    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Espera o serviço em segundo plano obter as chaves do emissor.</summary>
    public async Task WaitForIssuerKeysAsync()
    {
        _ = Server;
        var state = Services.GetRequiredService<IssuerKeysState>();
        await Poll.UntilAsync(() => state.Obtained, "as chaves do emissor não foram obtidas");
    }
}

public static class Poll
{
    public static async Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(25);
        }
    }

    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(25);
        }
    }
}
