using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// Host de teste. Sem rede: os dois <c>HttpClient</c> nomeados do Google têm o
/// handler primário trocado por <see cref="Google"/>. O conector falso entra só aqui
/// (chave <see cref="FakeConnector.Key"/>), nunca no Program.cs.
/// </summary>
public sealed class ConnectorsFactory(
    bool withFakeConnector = true,
    string? googleKeyBase64 = null,
    IReadOnlyDictionary<string, string?>? extraConfiguration = null,
    string environment = "Development") : WebApplicationFactory<Program>
{
    public GoogleDriveFakeHandler Google { get; } = new();

    public FakeConnector Fake { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

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
        };
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
        });
    }
}
