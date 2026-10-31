using Buteco.Api.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Buteco.Api.Users.Login;

/// <summary>
/// As duas barreiras de CSRF do <c>POST /auth/session</c> (design.md da change
/// usuarios-locais-login-emissor, D1), conferidas antes de qualquer verificação de senha:
/// <list type="number">
/// <item><b>só JSON</b> (<c>415</c>): um formulário HTML de outro site não manda JSON, e um
/// <c>fetch</c> de outra origem com JSON passa por preflight, que o CORS recusa;</item>
/// <item><b><c>Origin</c> igual à do issuer ou à de <c>Identity:PanelBaseUrl</c></b>
/// (<c>403</c>): ausente ou diferente, recusa. Cobre também um CORS aberto por engano.</item>
/// </list>
/// </summary>
public sealed class SessionRequestGuards(IdentityIssuerState issuer, IOptions<IdentityIssuerOptions> options)
{
    private readonly string[] _allowedOrigins = AllowedOrigins(issuer, options.Value).ToArray();

    public IResult? Reject(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !contentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(title: "O login aceita só application/json.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !_allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(title: "Origem não permitida.", statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static IEnumerable<string> AllowedOrigins(IdentityIssuerState issuer, IdentityIssuerOptions options)
    {
        if (issuer.Issuer is { } issuerUri)
        {
            yield return issuerUri.GetLeftPart(UriPartial.Authority);
        }

        if (Uri.TryCreate(options.PanelBaseUrl, UriKind.Absolute, out var panel))
        {
            yield return panel.GetLeftPart(UriPartial.Authority);
        }
    }
}
