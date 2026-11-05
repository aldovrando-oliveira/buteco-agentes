using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// O token que os três apps aceitavam até a #182: <c>base64url(payload).base64url(assinatura)</c>,
/// com a assinatura simétrica sobre o payload <c>{"Sub","Exp"}</c> e a chave que as fixtures
/// usavam. Existe só para provar que ele é recusado (change remover-hmac-token-servico, D1).
/// O formato é o do <c>TokenService</c> removido, copiado aqui porque o app não o tem mais.
/// </summary>
public static class OldSharedKeyToken
{
    // A chave das fixtures dos três apps até a #182, o mesmo texto nas três.
    public const string Key = "test-signing-key-shared-between-api-and-inbox-fixtures";

    public static string Sign(string subject = "operator", TimeSpan? lifetime = null)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(30)).ToUnixTimeSeconds();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(subject, expiresAt));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Key), payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Payload(string Sub, long Exp);
}
