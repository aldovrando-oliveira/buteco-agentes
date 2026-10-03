namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// Navegação e descrição de pasta no Google Drive (design.md, D7). O nível de cima é a
/// união de <c>drives.list</c> e das pastas em <c>sharedWithMe</c>; dentro de uma pasta,
/// as subpastas. As duas consultas do nível de cima são documentadas e não foram
/// medidas para service account na etapa 0.
/// </summary>
public sealed class GoogleDriveFolderNavigator(GoogleDriveClient client, string accountEmail) : IFolderNavigator
{
    private const string FolderFields = "id,name,webViewLink";

    public async Task<IReadOnlyList<FolderEntry>> BrowseAsync(string? parentId, CancellationToken cancellationToken)
    {
        try
        {
            if (parentId is null)
            {
                var drives = await client.ListSharedDrivesAsync(cancellationToken);
                var shared = await client.ListFilesAsync(
                    $"sharedWithMe = true and mimeType = '{GoogleDriveClient.FolderMimeType}' and trashed = false",
                    FolderFields,
                    cancellationToken);

                return
                [
                    .. drives.Select(drive => new FolderEntry(drive.Id, drive.Name, FolderEntryKind.SharedDrive, GoogleDriveFolders.FolderUrl(drive.Id))),
                    .. shared.Select(ToEntry),
                ];
            }

            await GoogleDriveFolders.ReadFolderAsync(client, parentId, accountEmail, cancellationToken);
            var children = await client.ListFilesAsync(
                $"{Quote(parentId)} in parents and mimeType = '{GoogleDriveClient.FolderMimeType}' and trashed = false",
                FolderFields,
                cancellationToken);
            return [.. children.Select(ToEntry)];
        }
        catch (GoogleApiError error)
        {
            throw GoogleDriveErrorMapper.Map(error, GoogleErrorTarget.Folder, accountEmail);
        }
    }

    public async Task<FolderDescription> DescribeFolderAsync(string folderId, CancellationToken cancellationToken)
    {
        var folder = await GoogleDriveFolders.ReadFolderAsync(client, folderId, accountEmail, cancellationToken);
        return new FolderDescription(folder.Id, folder.Name, folder.WebViewLink ?? GoogleDriveFolders.FolderUrl(folder.Id));
    }

    private static FolderEntry ToEntry(DriveFile folder) =>
        new(folder.Id, folder.Name, FolderEntryKind.Folder, folder.WebViewLink ?? GoogleDriveFolders.FolderUrl(folder.Id));

    // Literal de string da linguagem de consulta do Drive: aspas simples, com \ e '
    // escapados.
    internal static string Quote(string value) => $"'{value.Replace(@"\", @"\\").Replace("'", @"\'")}'";
}
