using System.Net;
using System.Text;
using Buteco.Inbox.Tests.Support;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Inbox.Tests;

/// <summary>
/// O que o token do emissor precisa trazer para valer no <c>apps/inbox</c>, e como o
/// principal é mapeado (spec <c>issuer-token-validation</c>; design.md da change
/// validacao-jwks-inbox-connectors, D4 a D6). Uma factory para a classe: nenhum
/// cenário daqui muda o estado das chaves.
/// </summary>
public sealed class IssuerTokenValidationTests(PostgresOnlyFixture postgres) : IClassFixture<PostgresOnlyFixture>, IAsyncLifetime
{
    private readonly TestIssuer _issuer = new();
    private IssuerTokenFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new IssuerTokenFactory(postgres, _issuer);
        await _factory.WaitForKeysAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task PanelToken_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetChannelsAsync(_issuer.Issue()));
    }

    [Fact]
    public async Task TokenWithTheThreeAudiences_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetChannelsAsync(_issuer.Issue(audiences: ["buteco-api", "buteco-inbox", "buteco-connectors"])));
    }

    [Fact]
    public async Task TokenForAnotherApp_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(audiences: ["buteco-connectors"])));
    }

    [Fact]
    public async Task WrongIssuer_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(issuer: "https://outro.exemplo.test/")));
    }

    // O iss é comparado depois da normalização com barra final, por igualdade ordinal.
    [Fact]
    public async Task IssuerWithoutTrailingSlash_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(issuer: TestIssuer.Issuer.TrimEnd('/'))));
    }

    [Fact]
    public async Task ExpiredBeyondTheClockSkew_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(expires: DateTime.UtcNow.AddSeconds(-60))));
    }

    [Fact]
    public async Task ExpiredWithinTheClockSkew_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, await GetChannelsAsync(_issuer.Issue(expires: DateTime.UtcNow.AddSeconds(-10))));
    }

    // Mesma chave publicada, outro algoritmo: só ValidAlgorithms recusa.
    [Fact]
    public async Task Rs512WithThePublishedKey_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(algorithm: SecurityAlgorithms.RsaSha512)));
    }

    [Fact]
    public async Task UnsignedToken_IsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(TestIssuer.IssueUnsigned()));
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("id_token+jwt")]
    public async Task WrongTokenType_IsUnauthorized(string tokenType)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(tokenType: tokenType)));
    }

    [Fact]
    public async Task ClientOutsideTheTable_IsForbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetChannelsAsync(_issuer.Issue(clientId: "cliente-desconhecido")));
    }

    [Fact]
    public async Task ForgedOperatorSub_DoesNotBecomeTheOperator()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetChannelsAsync(_issuer.Issue(clientId: "cliente-desconhecido", subject: "operator")));
    }

    [Fact]
    public async Task TokenWithoutClientId_IsForbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, await GetChannelsAsync(_issuer.Issue(clientId: null, subject: "operator")));
    }

    // O token que os apps aceitavam até a #182 (change remover-hmac-token-servico, D1): um
    // ponto só, assinatura simétrica com a chave das fixtures antigas.
    [Fact]
    public async Task HmacShapedToken_SignedWithTheOldKey_Is401()
    {
        var response = await _factory.CreateClientWithToken(OldSharedKeyToken.Sign()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    // O painel separa os dois 401 pelo corpo (design.md da change minha-conta-painel, D10;
    // decisão do dono Q0): o 401 com application/problem+json é a recusa da própria rota, e
    // volta à tela; o 401 de token, sem corpo, faz o apps/frontend/src/auth/sessionFetch.ts
    // renovar e repetir. Um corpo problem+json aqui faria o painel parar de renovar no token
    // vencido do apps/inbox. A #247 troca o critério por um campo de tipo.
    [Theory]
    [InlineData(null)]
    [InlineData("nao.e.um.token")]
    public async Task TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis(string? token)
    {
        var response = await _factory.CreateClientWithToken(token).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    // O token do painel sem papel recebe o mesmo 401 de token recusado, sem corpo problem+json
    // (design.md da change papeis-no-token, D3, Q12, com a condição do dono): é o que faz o
    // sessionFetch renovar e repetir, e a renovação traz o papel gravado. Os access tokens de antes
    // do deploy não têm papel; com 403, cada pessoa ficaria até 15 min sem acesso.
    [Theory]
    [InlineData(null)]
    [InlineData("superuser")]
    public async Task PanelTokenWithoutRole_401_HasNoProblemBody_ThePanelRenewsOnThis(string? role)
    {
        var token = role is null ? _issuer.Issue(withoutRole: true) : _issuer.Issue(role: role);
        var response = await _factory.CreateClientWithToken(token).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    private async Task<HttpStatusCode> GetChannelsAsync(string token) =>
        (await _factory.CreateClientWithToken(token).GetAsync("/channels")).StatusCode;
}
