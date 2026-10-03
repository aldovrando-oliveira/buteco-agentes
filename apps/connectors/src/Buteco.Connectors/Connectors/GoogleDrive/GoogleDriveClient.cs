using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// As chamadas à Drive API v3 que o conector usa, por REST (design.md, D2). Erro do
/// Google sai como <see cref="GoogleApiError"/>, e quem chama decide o código com
/// <see cref="GoogleDriveErrorMapper"/>, porque o mesmo <c>404</c> é "sem acesso" na
/// pasta e "sumiu" no arquivo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parâmetros de Drive Compartilhado (D7):</b> <c>supportsAllDrives=true</c> em todo
/// <c>files.get</c> (com e sem <c>alt=media</c>) e <c>files.list</c>, e
/// <c>includeItemsFromAllDrives=true</c> também em toda listagem de arquivos. Sem
/// <c>corpora</c>. <c>files.export</c> e <c>drives.list</c> não levam nenhum dos dois,
/// porque não são parâmetros deles.
/// </para>
/// <para>
/// Listagens leem todas as páginas, ou falham inteiras.
/// </para>
/// </remarks>
public sealed class GoogleDriveClient(
    IHttpClientFactory httpClientFactory,
    GoogleServiceAccountTokenSource tokenSource,
    ILogger<GoogleDriveClient> logger)
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    private const string FileFields = "id,name,mimeType,trashed,webViewLink";

    internal TimeSpan MetadataTimeout { get; init; } = GoogleDriveHttp.MetadataTimeout;

    internal TimeSpan ContentTimeout { get; init; } = GoogleDriveHttp.ContentTimeout;

    public async Task<DriveFile> GetFileAsync(string fileId, CancellationToken cancellationToken)
    {
        var json = await GetStringAsync(
            $"drive/v3/files/{Uri.EscapeDataString(fileId)}?fields={FileFields}&supportsAllDrives=true", MetadataTimeout, cancellationToken);
        return JsonSerializer.Deserialize<DriveFile>(json)!;
    }

    /// <summary><c>files.list</c> com a consulta e os campos dados, todas as páginas.</summary>
    public async Task<IReadOnlyList<DriveFile>> ListFilesAsync(string query, string fileFields, CancellationToken cancellationToken)
    {
        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var uri = $"drive/v3/files?q={Uri.EscapeDataString(query)}" +
                $"&fields={Uri.EscapeDataString($"nextPageToken,files({fileFields})")}" +
                "&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true" +
                (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var page = JsonSerializer.Deserialize<FileListPage>(await GetStringAsync(uri, MetadataTimeout, cancellationToken))!;
            files.AddRange(page.Files ?? []);
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return files;
    }

    /// <summary><c>drives.list</c>: os Drives Compartilhados de que a conta é membro.</summary>
    public async Task<IReadOnlyList<DriveSharedDrive>> ListSharedDrivesAsync(CancellationToken cancellationToken)
    {
        var drives = new List<DriveSharedDrive>();
        string? pageToken = null;
        do
        {
            var uri = "drive/v3/drives?pageSize=100&fields=nextPageToken,drives(id,name)" +
                (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var page = JsonSerializer.Deserialize<DriveListPage>(await GetStringAsync(uri, MetadataTimeout, cancellationToken))!;
            drives.AddRange(page.Drives ?? []);
            pageToken = page.NextPageToken;
        }
        while (pageToken is not null);

        return drives;
    }

    // files.export aceita só mimeType (referência oficial do método, consultada em
    // 03/10/2026): sem supportsAllDrives, ao contrário das outras chamadas (D7).
    public Task<string> ExportMarkdownAsync(string fileId, CancellationToken cancellationToken) =>
        GetStringAsync($"drive/v3/files/{Uri.EscapeDataString(fileId)}/export?mimeType=text%2Fmarkdown", ContentTimeout, cancellationToken);

    public Task<string> DownloadAsync(string fileId, CancellationToken cancellationToken) =>
        GetStringAsync($"drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true", ContentTimeout, cancellationToken);

    // O limite é por chamada (MetadataTimeout ou ContentTimeout), e não o
    // HttpClient.Timeout, que fica infinito no registro: o mesmo cliente serve as duas
    // naturezas. Inclui a leitura do corpo, que numa exportação grande é a parte lenta.
    private async Task<string> GetStringAsync(string relativeUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var token = await tokenSource.GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(GoogleDriveHttp.DriveBaseAddress, relativeUri));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            using var response = await httpClientFactory.CreateClient(GoogleDriveHttp.DriveClientName)
                .SendAsync(request, deadline.Token);
            var body = await response.Content.ReadAsStringAsync(deadline.Token);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var reason = ReadReason(body);
            logger.LogWarning("Drive API respondeu {StatusCode} com reason {Reason}.", (int)response.StatusCode, reason ?? "(nenhum)");
            throw new GoogleApiError((int)response.StatusCode, reason);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Chamada à Drive API sem resposta em {Timeout} ({ExceptionType}).", timeout, exception.GetType().Name);
            throw new GoogleApiError(0, null);
        }
    }

    // {"error":{"code","errors":[{"domain","reason","message"}],"message"}}
    // (developers.google.com/workspace/drive/api/guides/handle-errors, lida em 03/10/2026).
    private static string? ReadReason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0 &&
                errors[0].TryGetProperty("reason", out var reason) &&
                reason.ValueKind == JsonValueKind.String
                    ? reason.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record FileListPage(
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("files")] List<DriveFile>? Files);

    private sealed record DriveListPage(
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("drives")] List<DriveSharedDrive>? Drives);
}

/// <summary>Os campos de um arquivo do Drive que o conector lê. Datas ficam como vieram.</summary>
public sealed record DriveFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mimeType")] string? MimeType,
    [property: JsonPropertyName("trashed")] bool Trashed,
    [property: JsonPropertyName("webViewLink")] string? WebViewLink,
    [property: JsonPropertyName("modifiedTime")] string? ModifiedTime,
    [property: JsonPropertyName("md5Checksum")] string? Md5Checksum,
    [property: JsonPropertyName("capabilities")] DriveCapabilities? Capabilities);

public sealed record DriveCapabilities([property: JsonPropertyName("canDownload")] bool CanDownload);

public sealed record DriveSharedDrive(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);
