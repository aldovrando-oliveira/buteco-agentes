using System.Net;
using Buteco.Connectors.Connectors.GoogleDrive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// O conector Google montado sem host, sobre o <see cref="GoogleDriveFakeHandler"/>.
/// </summary>
public sealed class GoogleDriveTestKit
{
    public const string FolderPath = "/drive/v3/files/pasta-principal";

    public GoogleDriveTestKit(TimeSpan? metadataTimeout = null, TimeSpan? contentTimeout = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(GoogleDriveHttp.DriveClientName).ConfigurePrimaryHttpMessageHandler(() => Google);
        services.AddHttpClient(GoogleDriveHttp.OAuthClientName).ConfigurePrimaryHttpMessageHandler(() => Google);
        var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var key = GoogleServiceAccountKey.Parse(Key.Base64);
        var tokenSource = new GoogleServiceAccountTokenSource(key, httpClientFactory, TimeProvider.System, NullLogger<GoogleServiceAccountTokenSource>.Instance);
        var client = new GoogleDriveClient(httpClientFactory, tokenSource, NullLogger<GoogleDriveClient>.Instance)
        {
            MetadataTimeout = metadataTimeout ?? GoogleDriveHttp.MetadataTimeout,
            ContentTimeout = contentTimeout ?? GoogleDriveHttp.ContentTimeout,
        };
        Navigator = new GoogleDriveFolderNavigator(client, key.ClientEmail);
        ContentSource = new GoogleDriveFolderContentSource(client, key.ClientEmail);
    }

    public TestServiceAccountKey Key { get; } = TestServiceAccountKey.Create();

    public GoogleDriveFakeHandler Google { get; } = new();

    public GoogleDriveFolderNavigator Navigator { get; }

    public GoogleDriveFolderContentSource ContentSource { get; }

    /// <summary>A pasta principal existe e é pasta; a raiz é a da P3.</summary>
    public GoogleDriveTestKit WithMainFolder()
    {
        Google.OnPath(FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Fixture("raiz-p3.json"));
        return this;
    }

    public static HttpResponseMessage NotFound(string id) =>
        GoogleDriveFakeHandler.GoogleError(HttpStatusCode.NotFound, "notFound", $"File not found: {id}.");
}
