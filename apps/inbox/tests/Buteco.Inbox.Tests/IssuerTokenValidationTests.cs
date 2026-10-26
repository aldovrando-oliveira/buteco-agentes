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
    public async Task Hs256SignedWithTheHmacKey_IsUnauthorized()
    {
        var hmacKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestAuthentication.TokenSigningKey));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetChannelsAsync(_issuer.Issue(key: hmacKey, algorithm: SecurityAlgorithms.HmacSha256)));
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

    private async Task<HttpStatusCode> GetChannelsAsync(string token) =>
        (await _factory.CreateClientWithToken(token).GetAsync("/channels")).StatusCode;
}
