using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buteco.Connectors.Tests.Support;

/// <summary>
/// Chave de service account de teste: par RSA gerado por execução, nunca uma chave
/// real. O e-mail é fictício.
/// </summary>
public sealed class TestServiceAccountKey
{
    public const string ClientEmail = "conectores-teste@projeto-ficticio.iam.gserviceaccount.com";

    private TestServiceAccountKey(RSA rsa, string privateKeyPem, string privateKeyId, string json)
    {
        Rsa = rsa;
        PrivateKeyPem = privateKeyPem;
        PrivateKeyId = privateKeyId;
        Json = json;
        Base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public RSA Rsa { get; }

    public string PrivateKeyPem { get; }

    public string PrivateKeyId { get; }

    public string Json { get; }

    public string Base64 { get; }

    /// <summary>Um trecho de 40 caracteres do corpo do PEM, longe do cabeçalho.</summary>
    public string PemBodyFragment => PrivateKeyPem.Split('\n')[5][..40];

    public static TestServiceAccountKey Create(Action<Dictionary<string, object?>>? mutate = null)
    {
        var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem() + "\n";
        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

        // Os campos de um arquivo de chave como o Google o entrega.
        var fields = new Dictionary<string, object?>
        {
            ["type"] = "service_account",
            ["project_id"] = "projeto-ficticio",
            ["private_key_id"] = keyId,
            ["private_key"] = pem,
            ["client_email"] = ClientEmail,
            ["client_id"] = "100000000000000000001",
            ["auth_uri"] = "https://accounts.google.com/o/oauth2/auth",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        };
        mutate?.Invoke(fields);

        var json = JsonSerializer.Serialize(fields, new JsonSerializerOptions { WriteIndented = true });
        return new TestServiceAccountKey(rsa, pem, keyId, json);
    }

    public static string ToBase64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}
