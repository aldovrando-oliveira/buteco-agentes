extern alias ApiAssembly;

using System.Net;
using System.Net.Http.Headers;
using ApiConnectorsRoundTrip.Tests.Support;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using ApiServiceAccessTokenIssuer = ApiAssembly::Buteco.Api.Identity.ServiceTokens.ServiceAccessTokenIssuer;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// Acordo do token que o <c>apps/api</c> gera no processo (spec
/// <c>service-client-credentials</c>, "O apps/connectors real aceita o token gerado no
/// processo"; design.md da change servicos-token-emissor, D1; convenção 11): o
/// <c>JwtBearer</c> do <c>apps/connectors</c> real, com as chaves obtidas do <c>apps/api</c>
/// real pelo endereço interno, aceita o token na rota de descrição de pasta, e a tabela de
/// subjects o recusa nas rotas do operador.
/// </summary>
public class ServiceAccessTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    [Fact]
    public async Task TheTokenGeneratedInProcess_IsAcceptedByTheRealConnectors_OnlyOnTheFolderRoute()
    {
        var (api, connectors) = fixture.Build();

        // Uma requisição ao apps/api passa pelo gate e carrega o anel de chaves.
        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().GetAsync("/.well-known/jwks")).StatusCode);
        var token = await api.Services.GetRequiredService<ApiServiceAccessTokenIssuer>().GetAsync(CancellationToken.None);

        var client = connectors.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // A primeira busca de chaves do apps/connectors pode ter sido antes de o anel existir;
        // o 503 é o "ainda sem chave", e a próxima tentativa resolve.
        var folder = await PollAsync(
            () => client.GetAsync($"/connectors/providers/{FakeConnector.Key}/folder?id=pasta-do-acordo"),
            response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        Assert.NotEqual(HttpStatusCode.Unauthorized, folder.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, folder.StatusCode);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, folder.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/connectors/providers")).StatusCode);
    }

    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await send();
            if (done(response) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(200);
        }
    }
}
