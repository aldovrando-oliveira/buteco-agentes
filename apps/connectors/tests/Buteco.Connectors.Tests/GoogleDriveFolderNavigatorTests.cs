using System.Net;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Navegação por Drives Compartilhados e pastas
// compartilhadas com a conta" e "Pasta sem acesso responde erro". drives.list e
// sharedWithMe são documentados, não medidos (design.md, D7).
public class GoogleDriveFolderNavigatorTests
{
    private readonly GoogleDriveTestKit _kit = new();

    [Fact]
    public async Task NivelDeCima_DriveCompartilhadoEPastaCompartilhada()
    {
        _kit.Google.OnPath("/drive/v3/drives", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK,
            """{"drives":[{"id":"drive-equipe","name":"Equipe"}]}"""));
        _kit.Google.OnList("sharedWithMe = true", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK,
            """{"files":[{"id":"pasta-compartilhada","name":"Compartilhada","webViewLink":"https://drive.google.com/drive/folders/pasta-compartilhada"}]}"""));

        var entries = await _kit.Navigator.BrowseAsync(null, CancellationToken.None);

        Assert.Contains(new FolderEntry("drive-equipe", "Equipe", FolderEntryKind.SharedDrive, "https://drive.google.com/drive/folders/drive-equipe"), entries);
        Assert.Contains(new FolderEntry("pasta-compartilhada", "Compartilhada", FolderEntryKind.Folder, "https://drive.google.com/drive/folders/pasta-compartilhada"), entries);
        Assert.Equal(2, entries.Count);
        var shared = Assert.Single(_kit.Google.DriveRequests, request => request.Uri.AbsolutePath == "/drive/v3/files");
        Assert.Equal("sharedWithMe = true and mimeType = 'application/vnd.google-apps.folder' and trashed = false", shared.Query("q"));
    }

    [Fact]
    public async Task NivelDeCimaVazio_ListaVazia()
    {
        _kit.Google.OnPath("/drive/v3/drives", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"drives":[]}"""));
        _kit.Google.OnList("sharedWithMe = true", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        Assert.Empty(await _kit.Navigator.BrowseAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task DentroDeUmaPasta_LeAPastaAntesELista()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        _kit.Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK,
            """{"files":[{"id":"pasta-sub","name":"sub_pasta","webViewLink":"https://drive.google.com/drive/folders/pasta-sub"}]}"""));

        var entries = await _kit.Navigator.BrowseAsync("pasta-principal", CancellationToken.None);

        Assert.Equal([new FolderEntry("pasta-sub", "sub_pasta", FolderEntryKind.Folder, "https://drive.google.com/drive/folders/pasta-sub")], entries);
        var requests = _kit.Google.DriveRequests.ToList();
        Assert.Equal(GoogleDriveTestKit.FolderPath, requests[0].Uri.AbsolutePath);
        Assert.Equal("'pasta-principal' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false", requests[1].Query("q"));
    }

    [Fact]
    public async Task DentroDeUmaPastaSemSubpastas_ListaVaziaSemErro()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        _kit.Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        Assert.Empty(await _kit.Navigator.BrowseAsync("pasta-principal", CancellationToken.None));
    }

    [Fact]
    public async Task DentroDePastaSemAcesso_AccessDenied_NuncaListaVazia()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveTestKit.NotFound("pasta-principal"));
        _kit.Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.Navigator.BrowseAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("access-denied", failure.Code);
    }

    [Fact]
    public async Task Descricao_NomeEUrl()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));

        var description = await _kit.Navigator.DescribeFolderAsync("pasta-principal", CancellationToken.None);

        Assert.Equal(new FolderDescription("pasta-principal", "buteco_agentes_teste", "https://drive.google.com/drive/folders/pasta-principal"), description);
    }

    [Fact]
    public async Task NivelDeCimaComPaginacao_LeTodasAsPaginas()
    {
        _kit.Google.OnPath("/drive/v3/drives", request => GoogleDriveFakeHandler.Query(request, "pageToken") is null
            ? GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"nextPageToken":"p2","drives":[{"id":"d1","name":"D1"}]}""")
            : GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"drives":[{"id":"d2","name":"D2"}]}"""));
        _kit.Google.OnList("sharedWithMe = true", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        var entries = await _kit.Navigator.BrowseAsync(null, CancellationToken.None);

        Assert.Equal(["d1", "d2"], entries.Select(entry => entry.Id).Order());
    }
}
