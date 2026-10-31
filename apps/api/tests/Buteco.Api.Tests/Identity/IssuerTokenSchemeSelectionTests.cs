using Buteco.Api.Auth;
using Buteco.Api.Identity.Validation;
using Microsoft.AspNetCore.Http;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A forma do token escolhe o esquema (spec <c>route-authentication</c>, "apps/api aceita o
/// access token do emissor ao lado do HMAC"; design.md da change
/// usuarios-locais-login-emissor, D3.3). Que um JWT inválido não chega ao validador HMAC é
/// do <c>IssuerTokenAcceptanceTests</c>, com o host real.
/// </summary>
public class IssuerTokenSchemeSelectionTests
{
    private static string Select(string? authorization)
    {
        var context = new DefaultHttpContext();
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return IssuerTokenAuthentication.SelectScheme(context);
    }

    [Theory]
    [InlineData("Bearer aaa.bbb.ccc")]
    [InlineData("bearer aaa.bbb.ccc")]
    [InlineData("Bearer  aaa.bbb.ccc ")]
    [InlineData("Bearer a.b.c")]
    public void ThreeSegments_GoToTheIssuerScheme(string authorization)
    {
        Assert.Equal(IssuerTokenAuthentication.IssuerSchemeName, Select(authorization));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer payload.assinatura")]
    [InlineData("Bearer semponto")]
    [InlineData("Bearer a.b.c.d.e")]
    [InlineData("Basic aaa.bbb.ccc")]
    public void AnythingElse_GoesToTheHmacScheme(string? authorization)
    {
        Assert.Equal(OperatorTokenAuthenticationHandler.SchemeName, Select(authorization));
    }
}
