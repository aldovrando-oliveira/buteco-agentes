using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Auth;

// Token stateless assinado com HMAC-SHA256, não um JWT de biblioteca
// (design.md, Decision 1) — base64url(payload JSON) + "." +
// base64url(HMACSHA256(payload, TokenSigningKey)). Duplicado
// deliberadamente em apps/inbox, sem libs/ (Decision 2).
public sealed class TokenService(IOptions<TokenSigningOptions> options) : ITokenService
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(subject, expiresAt.ToUnixTimeSeconds()));
        var signature = Sign(payload);

        var token = $"{Base64UrlEncode(payload)}.{Base64UrlEncode(signature)}";
        return (token, expiresAt);
    }

    public TokenValidationResult Validate(string token)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return TokenValidationResult.Invalid;
        }

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signatureBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return TokenValidationResult.Invalid;
        }

        var expectedSignature = Sign(payloadBytes);
        if (signatureBytes.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignature))
        {
            return TokenValidationResult.Invalid;
        }

        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return TokenValidationResult.Invalid;
        }

        if (payload is null || DateTimeOffset.FromUnixTimeSeconds(payload.Exp) <= DateTimeOffset.UtcNow)
        {
            return TokenValidationResult.Invalid;
        }

        return new TokenValidationResult(true, payload.Sub);
    }

    private byte[] Sign(byte[] payload)
    {
        var key = Encoding.UTF8.GetBytes(options.Value.TokenSigningKey);
        return HMACSHA256.HashData(key, payload);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        return Convert.FromBase64String(padded);
    }

    private sealed record TokenPayload(string Sub, long Exp);
}
