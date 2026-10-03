using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Auth;

// Único esquema de autenticação do app, copiado do apps/inbox: valida a
// assinatura e põe o subject em ClaimTypes.NameIdentifier. Quem decide o que cada
// subject alcança é ConnectorsSubjectAuthorizationHandler (design.md, D1); este
// esquema só autentica.
public sealed class OperatorTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OperatorToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        var result = tokenService.Validate(token);
        if (!result.IsValid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Token inválido ou expirado."));
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, result.Subject!) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
