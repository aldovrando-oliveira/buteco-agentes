using System.Net;
using System.Text.Json;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// connectors-api, "Rotas sob o prefixo /connectors" e "Erros com código e status por
// natureza". Asserções sobre o TEXTO do JSON (convenção 12), não por desserialização
// no mesmo tipo.
public class ConnectorEndpointsTests
{
    [Fact]
    public async Task Provedores_ChavesEEmail_OrdenadosPorChave()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(googleKeyBase64: key.Base64);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var json = await client.GetStringAsync("/connectors/providers");

        Assert.Equal(
            $$"""[{"key":"fake","accountEmail":"{{FakeConnector.AccountEmail}}"},{"key":"google-drive","accountEmail":"{{TestServiceAccountKey.ClientEmail}}"}]""",
            json);
    }

    [Fact]
    public async Task SemCredencial_ProvedoresVazio()
    {
        await using var factory = new ConnectorsFactory(withFakeConnector: false);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        Assert.Equal("[]", await client.GetStringAsync("/connectors/providers"));
    }

    [Fact]
    public async Task Navegacao_FormatoDeFioEOrdenacao()
    {
        await using var factory = new ConnectorsFactory();
        factory.Fake.Browse = _ =>
        [
            new FolderEntry("id-2", "beta", FolderEntryKind.Folder, "https://falso.test/2"),
            new FolderEntry("id-1", "beta", FolderEntryKind.Folder, "https://falso.test/1"),
            new FolderEntry("id-3", "Alfa", FolderEntryKind.SharedDrive, "https://falso.test/3"),
        ];
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var json = await client.GetStringAsync("/connectors/providers/fake/folders");

        // Ordinal: "Alfa" (A maiúsculo) antes de "beta"; desempate por id.
        Assert.Equal(
            """[{"id":"id-3","name":"Alfa","kind":"SharedDrive","webUrl":"https://falso.test/3"},{"id":"id-1","name":"beta","kind":"Folder","webUrl":"https://falso.test/1"},{"id":"id-2","name":"beta","kind":"Folder","webUrl":"https://falso.test/2"}]""",
            json);
    }

    [Fact]
    public async Task Navegacao_RepassaOParentId()
    {
        await using var factory = new ConnectorsFactory();
        string? received = "nao-chamado";
        factory.Fake.Browse = parentId => { received = parentId; return []; };
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        await client.GetStringAsync("/connectors/providers/fake/folders?parentId=pasta-x");
        Assert.Equal("pasta-x", received);

        await client.GetStringAsync("/connectors/providers/fake/folders");
        Assert.Null(received);
    }

    [Fact]
    public async Task Descricao_FormatoDeFio()
    {
        await using var factory = new ConnectorsFactory();
        var client = TestAuthentication.CreateClientAs(factory, "service:api");

        var json = await client.GetStringAsync("/connectors/providers/fake/folder?id=pasta-1");

        Assert.Equal("""{"id":"pasta-1","name":"Pasta falsa","webUrl":"https://falso.test/pasta-1"}""", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?id=")]
    [InlineData("?id=%20")]
    public async Task DescricaoSemId_400(string query)
    {
        await using var factory = new ConnectorsFactory();
        var client = TestAuthentication.CreateClientAs(factory, "service:api");

        var response = await client.GetAsync($"/connectors/providers/fake/folder{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProvedorNaoConfigurado_404ComCodigo()
    {
        await using var factory = new ConnectorsFactory();
        var client = TestAuthentication.CreateClientAs(factory, "service:api");

        var response = await client.GetAsync("/connectors/providers/onedrive/folder?id=x");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("provider-not-configured", Code(await response.Content.ReadAsStringAsync()));
    }

    public static TheoryData<string, string?, HttpStatusCode> Falhas() => new()
    {
        { "access-denied", "conta@conectores.test", HttpStatusCode.UnprocessableEntity },
        { "not-a-folder", null, HttpStatusCode.UnprocessableEntity },
        { "folder-trashed", null, HttpStatusCode.UnprocessableEntity },
        { "api-not-configured", null, HttpStatusCode.BadGateway },
        { "provider-auth-failed", null, HttpStatusCode.BadGateway },
        { "provider-error", "bad-request", HttpStatusCode.BadGateway },
        { "rate-limited", null, HttpStatusCode.ServiceUnavailable },
        { "provider-unavailable", null, HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(Falhas))]
    public async Task FalhaDoConector_StatusPorNaturezaECodigo(string code, string? detail, HttpStatusCode expected)
    {
        await using var factory = new ConnectorsFactory();
        factory.Fake.Browse = _ => throw new ConnectorFailure(code, detail);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.GetAsync("/connectors/providers/fake/folders?parentId=p");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(expected, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(code, body.GetProperty("code").GetString());
        if (detail is null)
        {
            Assert.False(body.TryGetProperty("detail", out _));
        }
        else
        {
            Assert.Equal(detail, body.GetProperty("detail").GetString());
        }
    }

    // Pelo caminho inteiro com o Google falso: pasta sem acesso é 422 com o e-mail, e
    // NÃO 200 com [] (que é o que files.list devolveria).
    [Fact]
    public async Task GoogleNavegacaoEmPastaSemAcesso_422ENaoListaVazia()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(googleKeyBase64: key.Base64);
        factory.Google.OnPath("/drive/v3/files/pasta-x", _ => GoogleDriveTestKit.NotFound("pasta-x"));
        factory.Google.OnList("'pasta-x' in parents", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.GetAsync("/connectors/providers/google-drive/folders?parentId=pasta-x");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotEqual("[]", text);
        Assert.Equal("access-denied", Code(text));
        Assert.Equal(TestServiceAccountKey.ClientEmail, JsonDocument.Parse(text).RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GoogleDriveApiDesligada_502ENao403()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(googleKeyBase64: key.Base64);
        factory.Google.OnPath("/drive/v3/files/pasta-x", _ => GoogleDriveFakeHandler.GoogleError(
            HttpStatusCode.Forbidden, "accessNotConfigured", "Google Drive API has not been used in project 000000000000 before or it is disabled.", "usageLimits"));
        var client = TestAuthentication.CreateClientAs(factory, "service:api");

        var response = await client.GetAsync("/connectors/providers/google-drive/folder?id=pasta-x");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("api-not-configured", Code(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task GoogleCota_503()
    {
        var key = TestServiceAccountKey.Create();
        await using var factory = new ConnectorsFactory(googleKeyBase64: key.Base64);
        factory.Google.OnPath("/drive/v3/drives", _ => GoogleDriveFakeHandler.GoogleError(
            HttpStatusCode.Forbidden, "userRateLimitExceeded", "User Rate Limit Exceeded", "usageLimits"));
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.GetAsync("/connectors/providers/google-drive/folders");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("rate-limited", Code(await response.Content.ReadAsStringAsync()));
    }

    private static string? Code(string json) => JsonDocument.Parse(json).RootElement.GetProperty("code").GetString();
}
