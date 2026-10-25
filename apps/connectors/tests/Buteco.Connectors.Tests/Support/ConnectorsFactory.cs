using System.Net.Http.Headers;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// Host de teste. Sem rede: os dois <c>HttpClient</c> nomeados do Google têm o
/// handler primário trocado por <see cref="Google"/>. O conector falso entra só aqui
/// (chave <see cref="FakeConnector.Key"/>), nunca no Program.cs.
/// <para>
/// Com <paramref name="issuer"/>, a validação do token do emissor fica ligada contra o
/// <see cref="TestIssuer"/> (change validacao-jwks-inbox-connectors, D9):
/// <c>Identity:Issuer</c> e <c>Api:BaseUrl</c> dele, o cliente <c>IssuerKeys</c> com o
/// handler dele e o <paramref name="issuerKeysSchedule"/>. O <c>Api:BaseUrl</c> liga
/// também o ciclo de sincronização, contra o <see cref="SyncApi"/>. O
/// <see cref="ITokenService"/> é embrulhado num contador, para os testes afirmarem que um
/// JWT nunca passa pelo HMAC. Sem ele, <c>Identity:Issuer</c> fica vazio.
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

    public GoogleDriveFakeHandler Google { get; } = new();

    public FakeConnector Fake { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public CountingTokenService TokenService => (CountingTokenService)Services.GetRequiredService<ITokenService>();

    /// <summary>
    /// A <see cref="IServiceCollection"/> real, capturada pelo ConfigureServices, que
    /// roda depois de todos os registros do Program.cs (convenção 8).
    /// </summary>
    public IServiceCollection? CapturedServices { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, e não ConfigureAppConfiguration: o Program.cs lê a chave de
        // assinatura, o CORS e a credencial do Google ANTES do Build(), e só o que
        // entra por UseSetting já está na configuração nesse ponto (medido: com
        // ConfigureAppConfiguration, o boot sem chave subia com a do
        // appsettings.Development.json).
        builder.UseEnvironment(environment);
        var values = new Dictionary<string, string?>
        {
            ["Auth:TokenSigningKey"] = TestAuthentication.TokenSigningKey,
            ["Cors:AllowedOrigins:0"] = "http://painel.test",
            // Vazio conta como ausente: o ciclo de sincronização não roda (design.md da
            // change ciclo-de-sincronizacao, D2). Sem isto, o Api:BaseUrl do
            // appsettings.Development.json valeria para a suíte inteira, e cada teste
            // ligaria o ciclo contra localhost:5017. Só os testes do ciclo o configuram.
            ["Api:BaseUrl"] = string.Empty,
            // Mesmo motivo, para a validação do token do emissor
            // (validacao-jwks-inbox-connectors, D7): o appsettings.Development.json a liga.
            ["Identity:Issuer"] = string.Empty,
        };
        if (issuer is not null)
        {
            values["Identity:Issuer"] = TestIssuer.Issuer;
            values["Api:BaseUrl"] = TestIssuer.InternalBaseUrl;
        }

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

            if (issuer is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(issuerKeysSchedule ?? new IssuerKeysSchedule()));
                services.AddHttpClient(IssuerKeysRetriever.HttpClientName).ConfigurePrimaryHttpMessageHandler(issuer.CreateHandler);
                services.Replace(ServiceDescriptor.Singleton<ITokenService>(provider =>
                    new CountingTokenService(new TokenService(provider.GetRequiredService<IOptions<TokenSigningOptions>>()))));
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

public sealed class CountingTokenService(ITokenService inner) : ITokenService
{
    private int _validateCalls;

    public int ValidateCalls => Volatile.Read(ref _validateCalls);

    public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime) => inner.Issue(subject, lifetime);

    public TokenValidationResult Validate(string token)
    {
        Interlocked.Increment(ref _validateCalls);
        return inner.Validate(token);
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
