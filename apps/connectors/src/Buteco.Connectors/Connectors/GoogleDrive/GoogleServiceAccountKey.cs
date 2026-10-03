using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// A chave da service account, lida de <c>GoogleDrive:ServiceAccountKeyBase64</c>
/// (design.md, D3). É o único tipo que guarda a chave privada.
/// </summary>
/// <remarks>
/// <para>
/// Nenhuma mensagem de erro inclui trecho do valor nem exceção interna: a mensagem de
/// uma <see cref="JsonException"/> ou de uma <see cref="CryptographicException"/> não
/// é conteúdo nosso, e o boot a imprimiria. Por isso as exceções daqui não encadeiam a
/// original.
/// </para>
/// <para>
/// <see cref="ToString"/> não expõe campo nenhum, nem o e-mail: um log estruturado que
/// receba a instância não pode vazar a chave por engano.
/// </para>
/// </remarks>
public sealed class GoogleServiceAccountKey
{
    private const string Source = "GoogleDrive:ServiceAccountKeyBase64";

    private readonly string _privateKeyPem;

    private GoogleServiceAccountKey(string clientEmail, string privateKeyId, string privateKeyPem)
    {
        ClientEmail = clientEmail;
        PrivateKeyId = privateKeyId;
        _privateKeyPem = privateKeyPem;
    }

    /// <summary>O único dado da chave que sai do processo (rota de provedores).</summary>
    public string ClientEmail { get; }

    /// <summary>Vai no <c>kid</c> do JWT, e em nenhum outro lugar.</summary>
    internal string PrivateKeyId { get; }

    /// <summary>Uma instância nova a cada chamada: quem chama a descarta.</summary>
    internal RSA CreateRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(_privateKeyPem);
        return rsa;
    }

    public static GoogleServiceAccountKey Parse(string base64)
    {
        byte[] bytes;
        try
        {
            // O base64 pode chegar quebrado em linhas (base64 sem -w0); espaços e
            // quebras não fazem parte do valor.
            bytes = Convert.FromBase64String(string.Concat(base64.Where(c => !char.IsWhiteSpace(c))));
        }
        catch (FormatException)
        {
            throw Invalid("não é base64 válido");
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Invalid("não contém JSON válido");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("não contém um objeto JSON");
        }

        if (ReadString(root, "type") != "service_account")
        {
            throw Invalid("tem 'type' diferente de 'service_account'");
        }

        var clientEmail = ReadString(root, "client_email");
        if (string.IsNullOrWhiteSpace(clientEmail))
        {
            throw Invalid("não tem 'client_email'");
        }

        var privateKeyPem = ReadString(root, "private_key");
        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw Invalid("não tem 'private_key'");
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw Invalid("tem 'private_key' que não importa como chave RSA em PEM");
        }

        return new GoogleServiceAccountKey(clientEmail, ReadString(root, "private_key_id") ?? "", privateKeyPem);
    }

    public override string ToString() => nameof(GoogleServiceAccountKey);

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static InvalidOperationException Invalid(string what) => new($"{Source} {what}.");
}
