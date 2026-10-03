using System.Net;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// google-drive-connector: "Tipo suportado decidido pelo mimeType", "Markdown sem
// imagem embutida", "Pasta sem acesso responde erro", "Parâmetros de Drive
// Compartilhado"; connector-plugin: "Listagem completa ou erro".
public class GoogleDriveFolderContentSourceTests
{
    private readonly GoogleDriveTestKit _kit = new();

    [Fact]
    public async Task RaizDaP3_SuportadosComAVersaoCerta()
    {
        _kit.WithMainFolder();

        var listing = await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        Assert.Equal(
            [
                ("arquivo-doc-outro-dono", "2026-10-03T02:46:23.832Z"),
                ("arquivo-doc-longo", "2026-10-03T02:31:30.705Z"),
                ("arquivo-doc-tabela", "2026-10-03T02:23:18.358Z"),
                ("arquivo-doc-imagem", "2026-10-03T02:20:00.000Z"),
                ("arquivo-md-01", "b039972d4bd52b7c77ee21c89ceaeebf"),
                ("arquivo-md-02", "fdcdcff9b0ba038893f107c6481f6774"),
            ],
            listing.Files.Select(file => (file.ExternalRef, file.ExternalVersion)).ToList());
    }

    [Fact]
    public async Task RaizDaP3_IgnoradosComOCodigoCerto()
    {
        _kit.WithMainFolder();

        var listing = await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        Assert.Equal(
            [
                new IgnoredFile("arquivo-atalho", "Curriculo.docx", "shortcut-not-followed", null),
                new IgnoredFile("arquivo-planilha", "planilha_teste", "unsupported-type", "application/vnd.google-apps.spreadsheet"),
                new IgnoredFile("pasta-sub", "sub_pasta", "subfolder-not-synced", null),
                new IgnoredFile("arquivo-doc-bloqueado", "doc_bloqueado", "download-blocked", null),
            ],
            listing.Ignored);
    }

    [Fact]
    public async Task Ignorados_NenhumaExportacaoNemChamadaAoDestinoDoAtalho()
    {
        _kit.WithMainFolder();

        await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        var paths = _kit.Google.DriveRequests.Select(request => request.Uri.AbsolutePath).ToList();
        Assert.DoesNotContain(paths, path => path.EndsWith("/export", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, path => path.Contains("arquivo-destino-do-atalho", StringComparison.Ordinal));
        Assert.DoesNotContain(_kit.Google.DriveRequests, request => request.Query("alt") == "media");
        Assert.DoesNotContain(paths, path => path.Contains("arquivo-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Listagem_FiltraLixeiraELevaOsParametrosDeDriveCompartilhado()
    {
        _kit.WithMainFolder();

        await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        var list = Assert.Single(_kit.Google.DriveRequests, request => request.Uri.AbsolutePath == "/drive/v3/files");
        Assert.Equal("'pasta-principal' in parents and trashed = false", list.Query("q"));
        Assert.Equal("true", list.Query("supportsAllDrives"));
        Assert.Equal("true", list.Query("includeItemsFromAllDrives"));
        Assert.Null(list.Query("corpora"));
        var get = Assert.Single(_kit.Google.DriveRequests, request => request.Uri.AbsolutePath == GoogleDriveTestKit.FolderPath);
        Assert.Equal("true", get.Query("supportsAllDrives"));
    }

    [Fact]
    public async Task PastaSemAcesso_AccessDeniedComOEmail_ENenhumaListagem()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveTestKit.NotFound("pasta-principal"));
        // Se o conector listasse, a consulta "in parents" voltaria vazia com 200, que é
        // exatamente o que o Google faz numa pasta sem acesso.
        _kit.Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("access-denied", failure.Code);
        Assert.Equal(TestServiceAccountKey.ClientEmail, failure.Detail);
        Assert.DoesNotContain(_kit.Google.DriveRequests, request => request.Uri.AbsolutePath == "/drive/v3/files");
    }

    [Fact]
    public async Task PastaAcessivelEVazia_ListaVaziaSemErro()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        _kit.Google.OnList("'pasta-principal' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));

        var listing = await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        Assert.Empty(listing.Files);
        Assert.Empty(listing.Ignored);
    }

    [Fact]
    public async Task IdDeDocNoLugarDaPasta_NotAFolder()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK,
            """{"id":"pasta-principal","name":"doc","mimeType":"application/vnd.google-apps.document","trashed":false}"""));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("not-a-folder", failure.Code);
    }

    [Fact]
    public async Task PastaNaLixeira_FolderTrashed()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK,
            """{"id":"pasta-principal","name":"p","mimeType":"application/vnd.google-apps.folder","trashed":true}"""));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("folder-trashed", failure.Code);
    }

    [Fact]
    public async Task SegundaPaginaFalha_AOperacaoFalhaSemItensDaPrimeira()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        _kit.Google.OnList("'pasta-principal' in parents", request =>
            GoogleDriveFakeHandler.Query(request, "pageToken") is null
                ? GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"nextPageToken":"pagina-2","files":[{"id":"a","name":"a","mimeType":"application/vnd.google-apps.document","modifiedTime":"2026-10-03T00:00:00Z","capabilities":{"canDownload":true}}]}""")
                : GoogleDriveFakeHandler.GoogleError(HttpStatusCode.TooManyRequests, "rateLimitExceeded", "Rate Limit Exceeded", "usageLimits"));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("rate-limited", failure.Code);
        Assert.Equal(2, _kit.Google.DriveRequests.Count(request => request.Uri.AbsolutePath == "/drive/v3/files"));
    }

    [Fact]
    public async Task DuasPaginasComSucesso_JuntaAsDuas()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
        _kit.Google.OnList("'pasta-principal' in parents", request =>
            GoogleDriveFakeHandler.Query(request, "pageToken") is null
                ? GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"nextPageToken":"pagina-2","files":[{"id":"a","name":"a","mimeType":"application/vnd.google-apps.document","modifiedTime":"m1","capabilities":{"canDownload":true}}]}""")
                : GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[{"id":"b","name":"b","mimeType":"application/vnd.google-apps.document","modifiedTime":"m2","capabilities":{"canDownload":true}}]}"""));

        var listing = await _kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None);

        Assert.Equal(["a", "b"], listing.Files.Select(file => file.ExternalRef));
    }

    [Fact]
    public async Task DocComImagemEmEstiloDeReferencia_SaiSemAImagem()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-doc-imagem/export", _ => GoogleDriveFakeHandler.Fixture("export-doc-com-imagem.md"));
        var file = new RootFile("arquivo-doc-imagem", "doc_com_imagem", "v", "application/vnd.google-apps.document");

        var markdown = await _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None);

        Assert.DoesNotContain("data:image", markdown);
        Assert.DoesNotContain("![][image1]", markdown);
        Assert.DoesNotContain("[image1]:", markdown);
        Assert.Equal(ReadFixture("export-doc-com-imagem.esperado.md"), markdown);
    }

    // google-drive-connector, "Exportação só com mimeType": files.export não tem
    // supportsAllDrives na referência oficial (D7).
    [Fact]
    public async Task Exportacao_LevaSoMimeType()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-doc-tabela/export", _ => GoogleDriveFakeHandler.Fixture("export-doc-tabela-e-listas.md"));
        var file = new RootFile("arquivo-doc-tabela", "doc_tabela_e_listas", "v", "application/vnd.google-apps.document");

        await _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None);

        var export = Assert.Single(_kit.Google.DriveRequests);
        Assert.Equal("?mimeType=text%2Fmarkdown", export.Uri.Query);
        Assert.Null(export.Query("supportsAllDrives"));
    }

    [Fact]
    public async Task DocSemImagem_SaiIdenticoAoExportado_ComEscapes()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-doc-tabela/export", _ => GoogleDriveFakeHandler.Fixture("export-doc-tabela-e-listas.md"));
        var file = new RootFile("arquivo-doc-tabela", "doc_tabela_e_listas", "v", "application/vnd.google-apps.document");

        var markdown = await _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None);

        Assert.Equal(ReadFixture("export-doc-tabela-e-listas.md"), markdown);
        Assert.Contains(@"gerar\_arquivo\_texto\_1mb", markdown);
    }

    [Fact]
    public async Task MarkdownBaixado_ByteAByte_SemExportacao()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-md-01", request =>
            GoogleDriveFakeHandler.Query(request, "alt") == "media"
                ? GoogleDriveFakeHandler.Fixture("download-md.md")
                : new HttpResponseMessage((HttpStatusCode)599));
        var file = new RootFile("arquivo-md-01", "01.md", "md5", "text/x-markdown");

        var markdown = await _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None);

        Assert.Equal(ReadFixture("download-md.md"), markdown);
        var download = Assert.Single(_kit.Google.DriveRequests);
        Assert.Equal("true", download.Query("supportsAllDrives"));
    }

    [Fact]
    public async Task ExportacaoBloqueada_DownloadBlocked()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-doc-longo/export", _ =>
            GoogleDriveFakeHandler.GoogleError(HttpStatusCode.Forbidden, "cannotExportFile", "This file cannot be exported by the user."));
        var file = new RootFile("arquivo-doc-longo", "doc_longo", "v", "application/vnd.google-apps.document");

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None));

        Assert.Equal("download-blocked", failure.Code);
    }

    [Fact]
    public async Task ArquivoQueSumiuEntreListarEBaixar_FileNotFound()
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-doc-longo/export", _ => GoogleDriveTestKit.NotFound("arquivo-doc-longo"));
        var file = new RootFile("arquivo-doc-longo", "doc_longo", "v", "application/vnd.google-apps.document");

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => _kit.ContentSource.GetMarkdownAsync(file, CancellationToken.None));

        Assert.Equal("file-not-found", failure.Code);
    }

    // Medido contra o Drive real (tarefa 10.4): exportar um Doc de ~850 KB levou 16,8 s,
    // 26,1 s e mais de 30 s em três tentativas. Exportação e download têm limite próprio,
    // maior que o das leituras de metadado (design.md, D2).
    [Fact]
    public async Task ExportacaoLenta_PassaDoLimiteDeMetadado_SemFalhar()
    {
        var kit = new GoogleDriveTestKit(metadataTimeout: TimeSpan.FromMilliseconds(200), contentTimeout: TimeSpan.FromSeconds(5));
        kit.Google.OnPath("/drive/v3/files/arquivo-lento/export", _ => GoogleDriveFakeHandler.Text("# lento\n"));
        kit.Google.Delays["/drive/v3/files/arquivo-lento/export"] = TimeSpan.FromMilliseconds(600);

        var markdown = await kit.ContentSource.GetMarkdownAsync(
            new RootFile("arquivo-lento", "lento", "v", "application/vnd.google-apps.document"), CancellationToken.None);

        Assert.Equal("# lento\n", markdown);
    }

    [Fact]
    public async Task LeituraDeMetadadoLenta_ProviderUnavailablePeloLimiteCurto()
    {
        var kit = new GoogleDriveTestKit(metadataTimeout: TimeSpan.FromMilliseconds(200), contentTimeout: TimeSpan.FromSeconds(5));
        kit.WithMainFolder();
        kit.Google.Delays[GoogleDriveTestKit.FolderPath] = TimeSpan.FromMilliseconds(600);

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => kit.ContentSource.ListRootAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("provider-unavailable", failure.Code);
    }

    [Fact]
    public async Task ExportacaoAlemDoLimiteDeConteudo_ProviderUnavailable()
    {
        var kit = new GoogleDriveTestKit(metadataTimeout: TimeSpan.FromMilliseconds(200), contentTimeout: TimeSpan.FromMilliseconds(400));
        kit.Google.OnPath("/drive/v3/files/arquivo-lento/export", _ => GoogleDriveFakeHandler.Text("# lento\n"));
        kit.Google.Delays["/drive/v3/files/arquivo-lento/export"] = TimeSpan.FromSeconds(3);

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => kit.ContentSource.GetMarkdownAsync(
            new RootFile("arquivo-lento", "lento", "v", "application/vnd.google-apps.document"), CancellationToken.None));

        Assert.Equal("provider-unavailable", failure.Code);
    }

    internal static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GoogleDrive", name));
}
