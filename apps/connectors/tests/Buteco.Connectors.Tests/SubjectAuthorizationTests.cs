using System.Net;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

// connectors-api, "Autorização por tabela explícita de subjects". Token validamente
// assinado com a chave certa em toda célula: o que muda é só o subject.
public class SubjectAuthorizationTests : IAsyncLifetime
{
    private const string Providers = "/connectors/providers";
    private const string Folders = "/connectors/providers/fake/folders";
    private const string Folder = "/connectors/providers/fake/folder?id=pasta-1";

    private readonly ConnectorsFactory _factory = new();

    public static TheoryData<string, string, bool> Matrix()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var route in new[] { Providers, Folders, Folder })
        {
            data.Add("operator", route, route != Folder);
            data.Add("service:api", route, route == Folder);
            data.Add("service:inbox", route, false);
            data.Add("service:connectors", route, false);
            data.Add("qualquer", route, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task SubjectXRota(string subject, string route, bool allowed)
    {
        var client = TestAuthentication.CreateClientAs(_factory, subject);

        var response = await client.GetAsync(route);

        if (allowed)
        {
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task OperadorNaRotaExclusivaDoApi_Recebe403()
    {
        var client = TestAuthentication.CreateClientAs(_factory, "operator");

        var response = await client.GetAsync(Folder);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiNasRotasDoOperador_Recebe403()
    {
        var client = TestAuthentication.CreateClientAs(_factory, "service:api");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Providers)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Folders)).StatusCode);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();
}
