extern alias ApiAssembly;
extern alias ConnectorsAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiConnectorsRoundTrip.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using ConnectorsProgram = ConnectorsAssembly::Program;

namespace ApiConnectorsRoundTrip.Tests;

/// <summary>
/// O acordo da aplicação externa com o emissor real (change aplicacoes-externas-credencial, D2 e D8; 8.1
/// do tasks.md; convenção 11). O Admin do bootstrap entra pelo login real, cria a aplicação pela rota do
/// <c>apps/api</c> real, e a aplicação obtém token por <c>client_credentials</c> no mesmo emissor:
/// <list type="bullet">
/// <item>o <c>apps/connectors</c> recusa o token com <c>401</c>: a aplicação só recebe a audience
/// <c>buteco-api</c>, e o <c>apps/connectors</c> não muda nada para isso;</item>
/// <item>o <c>apps/api</c> o reconhece como <c>application</c> e responde <c>403</c>, não <c>401</c>, em
/// <c>GET /agents/</c>: o token vale, e a lista de rotas da aplicação tem só <c>POST /agents/{id}/a2a</c>
/// (change agentes-por-aplicacao, D2.1).</item>
/// </list>
/// </summary>
public class ExternalApplicationRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    [Fact]
    public async Task AnApplicationToken_Is401OnTheConnectors_And403OnTheApi()
    {
        var (api, connectors) = fixture.Build();
        var database = RoundTripFixture.IssuerDatabaseOf(api);
        Task WarmUp() => RoundTripFixture.WarmUpIssuerAsync(api);
        HttpClient Api() => api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var admin = await IssuerUserFlow.AccessTokenAsync(database, Api(), WarmUp, RoundTripFixture.ApiPublicBaseUrl, RoundTripFixture.AdminPassword, "openid buteco-api");

        var adminClient = Api();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        var created = await adminClient.PostAsJsonAsync("/applications/", new { nome = $"Acordo {Guid.NewGuid():N}"[..20], senha = RoundTripFixture.AdminPassword });
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.StatusCode == HttpStatusCode.Created, body.ToString());
        var clientId = body.GetProperty("clientId").GetString()!;
        var secret = body.GetProperty("segredo").GetProperty("valor").GetString()!;

        var tokenResponse = await Api().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
            ["scope"] = "buteco-api",
        }));
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(tokenResponse.IsSuccessStatusCode, tokenBody);
        var token = JsonDocument.Parse(tokenBody).RootElement.GetProperty("access_token").GetString()!;
        Assert.Equal(body.GetProperty("id").GetString(), new JsonWebToken(token).GetPayloadValue<string>("buteco_app"));

        // Primeiro as chaves no apps/connectors, com o token do Admin com a audience dele: o 503 é o
        // "ainda sem chave", e um kid fora do cache também daria 401, pelo motivo errado. Depois disso, o
        // 401 da aplicação só pode ser a audience.
        var adminConnectors = await IssuerUserFlow.AccessTokenAsync(database, Api(), WarmUp, RoundTripFixture.ApiPublicBaseUrl, RoundTripFixture.AdminPassword, "openid buteco-connectors");
        HttpResponseMessage adminResponse;
        var attempt = 0;
        do
        {
            adminResponse = await GetAsync(connectors, adminConnectors, "/connectors/providers");
            if (adminResponse.StatusCode is not (HttpStatusCode.ServiceUnavailable or HttpStatusCode.Unauthorized))
            {
                break;
            }

            await Task.Delay(100);
        }
        while (++attempt < 100);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);

        var connectorsResponse = await GetAsync(connectors, token, "/connectors/providers");
        var apiClient = Api();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var apiResponse = await apiClient.GetAsync("/agents/");

        Assert.Equal(HttpStatusCode.Unauthorized, connectorsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, apiResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> GetAsync(WebApplicationFactory<ConnectorsProgram> connectors, string token, string path)
    {
        var client = connectors.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }
}
