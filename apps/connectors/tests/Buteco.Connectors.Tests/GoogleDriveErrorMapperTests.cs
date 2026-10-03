using System.Net;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Erros do Google distinguidos pelo reason". Pelo caminho
// inteiro (fake HTTP → cliente → mapeamento), na leitura da pasta e na exportação.
public class GoogleDriveErrorMapperTests
{
    private readonly GoogleDriveTestKit _kit = new();

    // Os quatro 403 lado a lado: mesmo status, reasons diferentes, códigos diferentes.
    // accessNotConfigured e cannotExportFile com a mensagem medida na etapa 0.
    public static TheoryData<string, string, string> Os403() => new()
    {
        { "accessNotConfigured", "Google Drive API has not been used in project 000000000000 before or it is disabled.", "api-not-configured" },
        { "insufficientFilePermissions", "The user does not have sufficient permissions for file pasta-principal.", "access-denied" },
        { "userRateLimitExceeded", "User Rate Limit Exceeded", "rate-limited" },
        { "cannotExportFile", "This file cannot be exported by the user.", "download-blocked" },
    };

    [Theory]
    [MemberData(nameof(Os403))]
    public async Task MesmoStatus403_ReasonsDiferentes_CodigosDiferentes(string reason, string message, string expectedCode)
    {
        _kit.Google.OnPath("/drive/v3/files/arquivo-x/export", _ => GoogleDriveFakeHandler.GoogleError(HttpStatusCode.Forbidden, reason, message));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() =>
            _kit.ContentSource.GetMarkdownAsync(Doc("arquivo-x"), CancellationToken.None));

        Assert.Equal(expectedCode, failure.Code);
    }

    [Fact]
    public void OsQuatroCodigosSaoDiferentes()
    {
        var codes = Os403().Select(row => (string)row[2]).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    public static TheoryData<HttpStatusCode, string?, string, string?> NaPasta() => new()
    {
        { HttpStatusCode.NotFound, "notFound", "access-denied", TestServiceAccountKey.ClientEmail },
        { HttpStatusCode.Forbidden, "insufficientFilePermissions", "access-denied", TestServiceAccountKey.ClientEmail },
        { HttpStatusCode.Forbidden, "accessNotConfigured", "api-not-configured", null },
        { HttpStatusCode.Forbidden, "rateLimitExceeded", "rate-limited", null },
        { HttpStatusCode.TooManyRequests, "rateLimitExceeded", "rate-limited", null },
        { HttpStatusCode.Unauthorized, "authError", "provider-auth-failed", null },
        { HttpStatusCode.InternalServerError, "backendError", "provider-unavailable", null },
        { HttpStatusCode.ServiceUnavailable, null, "provider-unavailable", null },
        { HttpStatusCode.BadRequest, "badRequest", "provider-error", "bad-request" },
        { HttpStatusCode.BadRequest, "x!y", "provider-error", null },
        { HttpStatusCode.NotFound, null, "access-denied", TestServiceAccountKey.ClientEmail },
        { HttpStatusCode.TooManyRequests, null, "rate-limited", null },
        { HttpStatusCode.Conflict, null, "provider-error", null },
    };

    [Theory]
    [MemberData(nameof(NaPasta))]
    public async Task ErroAoLerAPasta(HttpStatusCode status, string? reason, string expectedCode, string? expectedDetail)
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => reason is null
            ? new HttpResponseMessage(status) { Content = new StringContent("<html>sem corpo json</html>") }
            : GoogleDriveFakeHandler.GoogleError(status, reason, "mensagem do Google"));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() =>
            _kit.Navigator.DescribeFolderAsync("pasta-principal", CancellationToken.None));

        Assert.Equal(expectedCode, failure.Code);
        Assert.Equal(expectedDetail, failure.Detail);
    }

    [Fact]
    public async Task Timeout_ProviderUnavailable()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => throw new TaskCanceledException("timeout simulado"));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() =>
            _kit.Navigator.DescribeFolderAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("provider-unavailable", failure.Code);
    }

    [Fact]
    public async Task FalhaDeRede_ProviderUnavailable()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ => throw new HttpRequestException("rede"));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() =>
            _kit.Navigator.DescribeFolderAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("provider-unavailable", failure.Code);
    }

    [Fact]
    public async Task MensagemDoGoogleNaoVaiParaODetalhe()
    {
        _kit.Google.OnPath(GoogleDriveTestKit.FolderPath, _ =>
            GoogleDriveFakeHandler.GoogleError(HttpStatusCode.BadRequest, "invalidQuery", "projeto-secreto-123 disse algo"));

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() =>
            _kit.Navigator.DescribeFolderAsync("pasta-principal", CancellationToken.None));

        Assert.Equal("invalid-query", failure.Detail);
        Assert.DoesNotContain("projeto-secreto", failure.ToString());
    }

    private static RootFile Doc(string id) => new(id, id, "v", "application/vnd.google-apps.document");
}
