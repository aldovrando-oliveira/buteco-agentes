using System.Net;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Um esquema por token, escolhido pela forma, sem queda de um no outro (spec
/// <c>issuer-token-validation</c>; design.md da change validacao-jwks-inbox-connectors,
/// D2). O HMAC continua exatamente como antes, e nunca toca nas chaves do emissor.
/// </summary>
public class IssuerTokenSchemeSelectionTests(PostgresOnlyFixture postgres) : IClassFixture<PostgresOnlyFixture>
{
    [Fact]
    public async Task OperatorHmacToken_StillAccepted_WithTheIssuerEnabled()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);

        var response = await factory.CreateClientWithToken(factory.OperatorHmacToken()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A busca em segundo plano fica fora da janela do teste (primeira tentativa no start,
    // a próxima só daqui a uma hora), e sem espera entre falhas: qualquer consulta ao
    // ConfigurationManager vinda da requisição viraria requisição HTTP contada aqui.
    [Fact]
    public async Task HmacTokens_ValidAndInvalid_NeverTouchTheIssuerKeys()
    {
        var issuer = new TestIssuer { Availability = IssuerAvailability.Unreachable };
        await using var factory = new IssuerTokenFactory(postgres, issuer, new IssuerKeysSchedule
        {
            FirstFetchRetryInterval = TimeSpan.FromHours(1),
            FailedFetchBackoff = TimeSpan.Zero,
        });
        _ = factory.Server;
        await Poll.UntilAsync(() => issuer.DiscoveryRequests == 1, "a primeira tentativa em segundo plano não aconteceu");
        var before = issuer.Requests.Count;

        var valid = await factory.CreateClientWithToken(factory.OperatorHmacToken()).GetAsync("/channels");
        var invalid = await factory.CreateClientWithToken("bm9wZQ.bm9wZQ").GetAsync("/channels");
        var anonymous = await factory.CreateClientWithToken(null).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(before, issuer.Requests.Count);
    }

    [Fact]
    public async Task InvalidJwt_IsUnauthorized_WithoutCallingTheHmacValidator()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();

        var hmac = await factory.CreateClientWithToken(factory.OperatorHmacToken()).GetAsync("/channels");
        var callsAfterHmac = factory.TokenService.ValidateCalls;
        var forged = await factory.CreateClientWithToken(issuer.Issue(key: TestIssuer.CreateKey())).GetAsync("/channels");
        var garbage = await factory.CreateClientWithToken("a.b.c").GetAsync("/channels");

        Assert.Equal(HttpStatusCode.OK, hmac.StatusCode);
        Assert.Equal(1, callsAfterHmac);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
        Assert.Equal(callsAfterHmac, factory.TokenService.ValidateCalls);
    }

    [Fact]
    public async Task AnonymousRoute_WithInvalidJwt_StillAnswersByTheRoute()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer);
        await factory.WaitForKeysAsync();

        var response = await factory.CreateClientWithToken("a.b.c").GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Bearer a.b.c", IssuerTokenAuthentication.IssuerSchemeName)]
    [InlineData("bearer a.b.c", IssuerTokenAuthentication.IssuerSchemeName)]
    [InlineData("Bearer a.b", OperatorTokenAuthenticationHandler.SchemeName)]
    [InlineData("Bearer a.b.c.d", OperatorTokenAuthenticationHandler.SchemeName)]
    [InlineData("Bearer abc", OperatorTokenAuthenticationHandler.SchemeName)]
    [InlineData("Basic a.b.c", OperatorTokenAuthenticationHandler.SchemeName)]
    [InlineData("", OperatorTokenAuthenticationHandler.SchemeName)]
    public void Selector_ChoosesBySegmentCount(string authorization, string expectedScheme)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (authorization.Length > 0)
        {
            context.Request.Headers.Authorization = authorization;
        }

        Assert.Equal(expectedScheme, IssuerTokenAuthentication.SelectScheme(context));
    }
}
