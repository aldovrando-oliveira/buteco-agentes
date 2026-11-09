extern alias ApiAssembly;
extern alias ConnectorsAssembly;

using System.Net;
using System.Net.Http.Headers;
using ApiConnectorsRoundTrip.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using ApiProgram = ApiAssembly::Program;
using ConnectorsProgram = ConnectorsAssembly::Program;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// Critério de aceite (a) da #184 (spec <c>route-roles</c>, "Critério de aceite da #184 no acordo com
/// o emissor real"; design.md da change papeis-no-token, D11; convenção 11): com o emissor real, o
/// token de um Leitor recebe <c>403</c> numa rota de pastas do <c>apps/connectors</c>, e o do Admin do
/// bootstrap não. O Leitor entra pelo convite e pelo login reais, com a verificação em 2 etapas.
/// </summary>
public class RoleRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string FoldersRoute = "/connectors/providers/google-drive/folders";
    private const string ReaderPassword = "senha-do-leitor-do-acordo";

    [Fact]
    public async Task AReader_Is403OnTheFolders_AndTheBootstrapAdminIsNot()
    {
        var (api, connectors) = fixture.Build();
        var database = RoundTripFixture.IssuerDatabaseOf(api);
        Task WarmUp() => RoundTripFixture.WarmUpIssuerAsync(api);
        HttpClient Api() => api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var adminConnectors = await IssuerUserFlow.AccessTokenAsync(database, Api(), WarmUp, RoundTripFixture.ApiPublicBaseUrl, RoundTripFixture.AdminPassword, "openid buteco-connectors");
        var adminApi = await IssuerUserFlow.AccessTokenAsync(database, Api(), WarmUp, RoundTripFixture.ApiPublicBaseUrl, RoundTripFixture.AdminPassword, "openid buteco-api");
        var readerEmail = $"leitor-{Guid.NewGuid():N}@exemplo.test";
        await IssuerUserFlow.InviteAndAcceptAsync(Api(), RoundTripFixture.ApiPublicBaseUrl, adminApi, RoundTripFixture.AdminPassword, readerEmail, "leitor", ReaderPassword);
        var reader = await IssuerUserFlow.AccessTokenAsync(database, Api(), WarmUp, RoundTripFixture.ApiPublicBaseUrl, ReaderPassword, "openid buteco-connectors", readerEmail);

        Assert.Equal("admin", new JsonWebToken(adminConnectors).GetPayloadValue<string>("role"));
        Assert.Equal("reader", new JsonWebToken(reader).GetPayloadValue<string>("role"));

        var adminResponse = await PollAsync(() => GetAsync(connectors, adminConnectors), response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        var readerResponse = await GetAsync(connectors, reader);

        Assert.NotEqual(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readerResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> GetAsync(WebApplicationFactory<ConnectorsProgram> connectors, string token)
    {
        var client = connectors.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(FoldersRoute);
    }

    // A primeira busca das chaves pode ter acontecido antes de o anel do apps/api existir; o 503 é
    // o "ainda sem chave", e a próxima tentativa resolve.
    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var response = await send();
            if (done(response))
            {
                return response;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("O apps/connectors não obteve as chaves do emissor a tempo.");
    }
}
