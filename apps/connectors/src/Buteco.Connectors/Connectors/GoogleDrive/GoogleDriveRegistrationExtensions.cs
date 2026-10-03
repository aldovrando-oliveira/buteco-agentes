namespace Buteco.Connectors.Connectors.GoogleDrive;

public static class GoogleDriveRegistrationExtensions
{
    public const string ProviderKey = "google-drive";

    /// <summary>
    /// Registra o conector Google sob <see cref="ProviderKey"/>, só se a credencial
    /// estiver presente (design.md, D3). Credencial presente e inválida derruba o boot
    /// aqui, com a mensagem de <see cref="GoogleServiceAccountKey.Parse"/>, que não
    /// carrega o valor.
    /// </summary>
    public static IServiceCollection AddGoogleDriveConnector(this IServiceCollection services, IConfiguration configuration)
    {
        var base64 = configuration.GetSection(GoogleDriveOptions.SectionName).Get<GoogleDriveOptions>()?.ServiceAccountKeyBase64;
        if (string.IsNullOrWhiteSpace(base64))
        {
            return services;
        }

        var key = GoogleServiceAccountKey.Parse(base64);

        // Sem limite no HttpClient da Drive API: o GoogleDriveClient põe um por chamada,
        // curto para metadado e longo para exportação e download (D2).
        services.AddHttpClient(GoogleDriveHttp.DriveClientName, client =>
        {
            client.BaseAddress = GoogleDriveHttp.DriveBaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddHttpClient(GoogleDriveHttp.OAuthClientName, client => client.Timeout = GoogleDriveHttp.MetadataTimeout);

        services.AddSingleton(provider => new GoogleServiceAccountTokenSource(
            key,
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<GoogleServiceAccountTokenSource>>()));
        services.AddSingleton<GoogleDriveClient>();

        return services.AddConnector(
            ProviderKey,
            key.ClientEmail,
            provider => new GoogleDriveFolderNavigator(provider.GetRequiredService<GoogleDriveClient>(), key.ClientEmail),
            provider => new GoogleDriveFolderContentSource(provider.GetRequiredService<GoogleDriveClient>(), key.ClientEmail));
    }
}
