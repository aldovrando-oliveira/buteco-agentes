using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiConnectorsRoundTrip.Tests.Support;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// Ida e volta entre o <c>apps/api</c> e o <c>apps/connectors</c> reais
/// (spec <c>knowledge-sync-folder-validation</c>, "Ida e volta entre apps/api e
/// apps/connectors reais"; design.md da change criacao-base-sincronizada, D8).
/// Nenhuma chamada ao Google: o provedor é o conector falso do <c>apps/connectors</c>.
/// </summary>
public partial class FolderValidationRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private static object SyncedRequest(string provider, string folderId) => new
    {
        name = $"Base de ida e volta {folderId}",
        description = "Base acompanhando uma pasta.",
        contentMode = "Synced",
        provider,
        folderId,
        folderName = "Nome forjado pelo corpo",
        folderUrl = "https://forjado.test",
    };

    private static string NewFolderId() => $"pasta-{Guid.NewGuid():N}";

    // O sucesso prova também o subject: a tabela do apps/connectors só aceita
    // service:api na rota de descrição, e o token é assinado pelo handler do apps/api.
    [Fact]
    public async Task CreateSynced_ThroughTheRealConnectors_StoresTheFakeConnectorFolder()
    {
        var (api, _) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var folderId = NewFolderId();
        fixture.Fake.Describe = id => new FolderDescription(id, "Pasta do conector falso", $"https://falso.test/{id}");

        var response = await RoundTripFixture.OperatorClient(api)
            .PostAsJsonAsync("/knowledge-bases", SyncedRequest(FakeConnector.Key, folderId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = Assert.Single(await RoundTripFixture.StoredFolderAsync(api, folderId));
        Assert.Equal("Pasta do conector falso", stored.Name);
        Assert.Equal($"https://falso.test/{folderId}", stored.Url);
    }

    [Fact]
    public async Task CreateSynced_ConnectorAccessDenied_CodeAndEmailCrossBothApps()
    {
        var (api, _) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var folderId = NewFolderId();
        fixture.Fake.Describe = _ => throw new ConnectorFailure("access-denied", FakeConnector.AccountEmail);

        var response = await RoundTripFixture.OperatorClient(api)
            .PostAsJsonAsync("/knowledge-bases", SyncedRequest(FakeConnector.Key, folderId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("access-denied", body.GetProperty("code").GetString());
        Assert.Equal(FakeConnector.AccountEmail, body.GetProperty("detail").GetString());
        Assert.Empty(await RoundTripFixture.StoredFolderAsync(api, folderId));
    }

    [Fact]
    public async Task CreateSynced_GoogleDriveNotConfiguredInTheRealConnectors_Is422ProviderNotConfigured()
    {
        var (api, _) = fixture.Build(RoundTripFixture.TokenSigningKey);
        var folderId = NewFolderId();

        var response = await RoundTripFixture.OperatorClient(api)
            .PostAsJsonAsync("/knowledge-bases", SyncedRequest("google-drive", folderId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("provider-not-configured", body.GetProperty("code").GetString());
        Assert.Empty(await RoundTripFixture.StoredFolderAsync(api, folderId));
    }

    [Fact]
    public async Task CreateSynced_ConnectorsWithADifferentSigningKey_Is502ConnectorsError401()
    {
        var (api, _) = fixture.Build("chave-de-assinatura-diferente-da-do-apps-api");
        var folderId = NewFolderId();

        var response = await RoundTripFixture.OperatorClient(api)
            .PostAsJsonAsync("/knowledge-bases", SyncedRequest(FakeConnector.Key, folderId));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("connectors-error", body.GetProperty("code").GetString());
        Assert.Equal("401", body.GetProperty("detail").GetString());
        Assert.Empty(await RoundTripFixture.StoredFolderAsync(api, folderId));
    }
}
