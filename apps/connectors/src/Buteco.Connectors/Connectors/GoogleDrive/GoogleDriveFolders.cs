namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// A leitura da pasta que toda operação sobre ela faz ANTES de consultar o conteúdo
/// (design.md, D5): <c>files.list</c> com <c>'id' in parents</c> numa pasta sem acesso
/// responde <c>200</c> com lista vazia, porque é uma consulta e não uma leitura.
/// </summary>
internal static class GoogleDriveFolders
{
    public static async Task<DriveFile> ReadFolderAsync(
        GoogleDriveClient client, string folderId, string accountEmail, CancellationToken cancellationToken)
    {
        DriveFile folder;
        try
        {
            folder = await client.GetFileAsync(folderId, cancellationToken);
        }
        catch (GoogleApiError error)
        {
            throw GoogleDriveErrorMapper.Map(error, GoogleErrorTarget.Folder, accountEmail);
        }

        if (folder.MimeType != GoogleDriveClient.FolderMimeType)
        {
            throw new ConnectorFailure(ConnectorCodes.NotAFolder);
        }

        if (folder.Trashed)
        {
            throw new ConnectorFailure(ConnectorCodes.FolderTrashed);
        }

        return folder;
    }

    // Drive Compartilhado não tem webViewLink no recurso de drives.list; esta é a forma
    // de URL de pasta do Drive web. Não medida na etapa 0 (verificação manual, 10.4).
    public static string FolderUrl(string id) => $"https://drive.google.com/drive/folders/{Uri.EscapeDataString(id)}";
}
