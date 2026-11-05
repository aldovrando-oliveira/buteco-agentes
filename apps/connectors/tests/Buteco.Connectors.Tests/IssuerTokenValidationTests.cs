using System.Net;
using System.Text;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O que o token do emissor precisa trazer para valer no <c>apps/connectors</c>, e como o
/// principal é mapeado (spec <c>issuer-token-validation</c>; design.md da change
/// validacao-jwks-inbox-connectors, D4 a D6). Uma factory para a classe: nenhum cenário
/// daqui muda o estado das chaves.
/// </summary>
public sealed class IssuerTokenValidationTests : IAsyncLifetime
{
    private const string OperatorRoute = "/connectors/providers";

    private readonly TestIssuer _issuer = new();
    private ConnectorsFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new ConnectorsFactory(issuer: _issuer);
        await _factory.WaitForIssuerKeysAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task PanelToken_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetAsync(_issuer.Issue()));
    }

    [Fact]
    public async Task PanelToken_CanSyncNow()
    {
        var response = await _factory.CreateClientWithToken(_issuer.Issue())
            .PostAsync($"/connectors/knowledge-bases/{Guid.NewGuid()}/sync", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithTheThreeAudiences_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetAsync(_issuer.Issue(audiences: ["buteco-api", "buteco-inbox", "buteco-connectors"])));
    }

    [Fact]
    public async Task TokenForAnotherApp_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(audiences: ["buteco-inbox"])));
    }

    [Fact]
    public async Task WrongIssuer_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(issuer: "https://outro.exemplo.test/")));
    }

    [Fact]
    public async Task IssuerWithoutTrailingSlash_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(issuer: TestIssuer.Issuer.TrimEnd('/'))));
    }

    [Fact]
    public async Task ExpiredBeyondTheClockSkew_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(expires: DateTime.UtcNow.AddSeconds(-60))));
    }

    [Fact]
    public async Task ExpiredWithinTheClockSkew_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetAsync(_issuer.Issue(expires: DateTime.UtcNow.AddSeconds(-10))));
    }

    [Fact]
    public async Task Rs512WithThePublishedKey_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(algorithm: SecurityAlgorithms.RsaSha512)));
    }

    [Fact]
    public async Task UnsignedToken_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(TestIssuer.IssueUnsigned()));
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("id_token+jwt")]
    public async Task WrongTokenType_IsUnauthorized(string tokenType)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(_issuer.Issue(tokenType: tokenType)));
    }

    [Fact]
    public async Task ClientOutsideTheTable_IsForbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(_issuer.Issue(clientId: "cliente-desconhecido")));
    }

    [Fact]
    public async Task ForgedOperatorSub_DoesNotBecomeTheOperator()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(_issuer.Issue(clientId: "cliente-desconhecido", subject: "operator")));
    }

    // service:api como sub não abre "descrever pasta": o sub nunca vira subject.
    [Fact]
    public async Task ForgedServiceApiSub_DoesNotOpenDescribeFolder()
    {
        var response = await _factory.CreateClientWithToken(_issuer.Issue(clientId: "cliente-desconhecido", subject: ConnectorsSubjectAuthorizationHandler.ApiSubject))
            .GetAsync("/connectors/providers/fake/folder?id=pasta-1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutClientId_IsForbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(_issuer.Issue(clientId: null, subject: "operator")));
    }

    // O subject mapeado segue a tabela de rotas: "descrever pasta" é só do service:api.
    [Fact]
    public async Task PanelToken_OnDescribeFolder_IsForbidden()
    {
        var issued = await _factory.CreateClientWithToken(_issuer.Issue()).GetAsync("/connectors/providers/google-drive/folder?id=x");

        Assert.Equal(HttpStatusCode.Forbidden, issued.StatusCode);
    }

    // O token que os apps aceitavam até a #182 (change remover-hmac-token-servico, D1): um
    // ponto só, assinatura simétrica com a chave das fixtures antigas.
    [Fact]
    public async Task HmacShapedToken_SignedWithTheOldKey_Is401()
    {
        var response = await _factory.CreateClientWithToken(OldSharedKeyToken.Sign()).GetAsync(OperatorRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    // O cliente de serviço do apps/api (design.md da change clientes-servico-emissor, D3).
    // Verificado contra o defeito (convenção 15): com a entrada buteco-servico-api retirada da
    // tabela, o caso de "descrever pasta" reprova com 403 (registro no tasks.md, 4.5).
    [Fact]
    public async Task ApiServiceClient_OpensDescribeFolder_AndIsForbiddenOnTheOperatorRoutes()
    {
        var client = _factory.CreateClientWithToken(_issuer.Issue(
            clientId: IssuerClientSubjects.ApiServiceClientId,
            subject: IssuerClientSubjects.ApiServiceClientId));

        var describe = await client.GetAsync("/connectors/providers/fake/folder?id=pasta-1");
        Assert.NotEqual(HttpStatusCode.Unauthorized, describe.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, describe.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/connectors/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/connectors/providers/fake/folders")).StatusCode);
    }

    [Fact]
    public async Task ApiServiceClient_WithoutTheConnectorsAudience_IsUnauthorized()
    {
        var response = await _factory.CreateClientWithToken(_issuer.Issue(
                audiences: ["buteco-api"],
                clientId: IssuerClientSubjects.ApiServiceClientId,
                subject: IssuerClientSubjects.ApiServiceClientId))
            .GetAsync("/connectors/providers/fake/folder?id=pasta-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpStatusCode> GetAsync(string token) =>
        (await _factory.CreateClientWithToken(token).GetAsync(OperatorRoute)).StatusCode;
}
