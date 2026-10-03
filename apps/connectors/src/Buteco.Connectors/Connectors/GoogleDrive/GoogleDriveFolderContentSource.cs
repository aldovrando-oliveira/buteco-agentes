namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// Raiz da pasta e markdown de arquivo no Google Drive, pelo que a etapa 0 mediu
/// (design.md, D4). Consumidor esperado: o ciclo da #105.
/// </summary>
public sealed class GoogleDriveFolderContentSource(GoogleDriveClient client, string accountEmail) : IFolderContentSource
{
    public const string GoogleDocMimeType = "application/vnd.google-apps.document";
    public const string ShortcutMimeType = "application/vnd.google-apps.shortcut";

    // text/x-markdown é o medido; text/markdown é o tipo registrado do markdown, não
    // observado na rodada.
    private static readonly HashSet<string> MarkdownMimeTypes = ["text/x-markdown", "text/markdown"];

    private const string RootFields = "id,name,mimeType,modifiedTime,md5Checksum,capabilities/canDownload";

    public async Task<RootListing> ListRootAsync(string folderId, CancellationToken cancellationToken)
    {
        await GoogleDriveFolders.ReadFolderAsync(client, folderId, accountEmail, cancellationToken);

        IReadOnlyList<DriveFile> items;
        try
        {
            items = await client.ListFilesAsync(
                $"{GoogleDriveFolderNavigator.Quote(folderId)} in parents and trashed = false", RootFields, cancellationToken);
        }
        catch (GoogleApiError error)
        {
            throw GoogleDriveErrorMapper.Map(error, GoogleErrorTarget.Folder, accountEmail);
        }

        var files = new List<RootFile>();
        var ignored = new List<IgnoredFile>();
        foreach (var item in items)
        {
            var (file, skip) = Classify(item);
            if (file is not null)
            {
                files.Add(file);
            }
            else
            {
                ignored.Add(skip!);
            }
        }

        return new RootListing(files, ignored);
    }

    public async Task<string> GetMarkdownAsync(RootFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file.MimeType == GoogleDocMimeType)
            {
                return GoogleDriveMarkdown.StripEmbeddedImages(await client.ExportMarkdownAsync(file.ExternalRef, cancellationToken));
            }

            if (MarkdownMimeTypes.Contains(file.MimeType))
            {
                return await client.DownloadAsync(file.ExternalRef, cancellationToken);
            }
        }
        catch (GoogleApiError error)
        {
            throw GoogleDriveErrorMapper.Map(error, GoogleErrorTarget.File, accountEmail);
        }

        throw new ArgumentException($"Tipo não suportado: '{file.MimeType}'. Só arquivos suportados da listagem.", nameof(file));
    }

    // Ordem (D4): pela forma do item (atalho, subpasta), depois tipo, depois
    // canDownload. Atalho também vem com canDownload=false, e o código certo dele é
    // shortcut-not-followed. Nunca pela extensão do nome: o atalho "Curriculo.docx"
    // aponta para um Google Doc.
    private static (RootFile? File, IgnoredFile? Ignored) Classify(DriveFile item)
    {
        IgnoredFile Ignore(string code, string? detail = null) => new(item.Id, item.Name, code, detail);

        if (item.MimeType == ShortcutMimeType)
        {
            return (null, Ignore(ConnectorCodes.ShortcutNotFollowed));
        }

        if (item.MimeType == GoogleDriveClient.FolderMimeType)
        {
            return (null, Ignore(ConnectorCodes.SubfolderNotSynced));
        }

        var isDoc = item.MimeType == GoogleDocMimeType;
        if (!isDoc && (item.MimeType is null || !MarkdownMimeTypes.Contains(item.MimeType)))
        {
            return (null, Ignore(ConnectorCodes.UnsupportedType, item.MimeType));
        }

        if (item.Capabilities is { CanDownload: false })
        {
            return (null, Ignore(ConnectorCodes.DownloadBlocked));
        }

        // ExternalVersion (D3 da change catalogo-base-sincronizada): modifiedTime para
        // Doc, md5Checksum para .md. Sem o marcador, o arquivo não tem como ser
        // reconciliado, e vai para os ignorados em vez de entrar com versão vazia.
        var version = isDoc ? item.ModifiedTime : item.Md5Checksum;
        if (string.IsNullOrEmpty(version))
        {
            return (null, Ignore(ConnectorCodes.ProviderError, isDoc ? "missing-modified-time" : "missing-md5-checksum"));
        }

        return (new RootFile(item.Id, item.Name, version, item.MimeType!), null);
    }
}
