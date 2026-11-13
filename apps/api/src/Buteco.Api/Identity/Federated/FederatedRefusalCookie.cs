using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O cookie <c>buteco-recusa</c> (design.md da change provedores-federados-google, D7; Q6): o e-mail que o
/// provedor confirmou, para o aviso da recusa, sem nunca ir na URL. <c>HttpOnly</c>, <c>Strict</c>, só no
/// caminho da rota que o lê, 120 s. O conteúdo é protegido pelo Data Protection; o instante da gravação vai
/// dentro, e o prazo é conferido pelo <see cref="TimeProvider"/> na leitura, além do <c>Max-Age</c>.
/// </summary>
public sealed class FederatedRefusalCookie(IDataProtectionProvider dataProtection, TimeProvider timeProvider, IHostEnvironment environment, IMemoryCache used)
{
    public const string Purpose = "Buteco.FederatedRefusal";

    public sealed record Content(string Motivo, string Provedor, string Email);

    private sealed record Payload(string M, string P, string E, long T, string I);

    private IDataProtector Protector => dataProtection.CreateProtector(Purpose);

    public void Write(HttpResponse response, string reason, string providerKey, string email)
    {
        var payload = JsonSerializer.Serialize(new Payload(reason, providerKey, email, timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));
        response.Cookies.Append(FederatedLogin.RefusalCookieName, Protector.Protect(payload), Options(response.HttpContext.Request));
    }

    /// <summary>O conteúdo, se o cookie existe, decifra e está no prazo; sempre o apaga (uso único).</summary>
    public Content? Take(HttpContext httpContext)
    {
        var value = httpContext.Request.Cookies[FederatedLogin.RefusalCookieName];
        httpContext.Response.Cookies.Delete(FederatedLogin.RefusalCookieName, Options(httpContext.Request));
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(Protector.Unprotect(value));
            if (payload is null)
            {
                return null;
            }

            var age = timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(payload.T);
            if (age < TimeSpan.Zero || age > FederatedLogin.RefusalCookieLifetime)
            {
                return null;
            }

            // Uso único também contra o mesmo valor reenviado: o id lido fica marcado pelo prazo do cookie.
            var mark = $"{Purpose}:{payload.I}";
            if (used.TryGetValue(mark, out _))
            {
                return null;
            }

            used.Set(mark, true, FederatedLogin.RefusalCookieLifetime);
            return new Content(payload.M, payload.P, payload.E);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    // Secure como o buteco-sessao: sempre fora de Development, e como a requisição nele.
    private CookieOptions Options(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = !environment.IsDevelopment() || request.IsHttps,
        Path = FederatedLogin.RefusalPath,
        MaxAge = FederatedLogin.RefusalCookieLifetime,
        IsEssential = true,
    };
}
