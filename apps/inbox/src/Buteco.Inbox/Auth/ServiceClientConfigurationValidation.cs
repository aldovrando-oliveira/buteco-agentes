using Buteco.Inbox.Channels.Security;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Auth;

/// <summary>
/// Checagem de boot do segredo do <c>apps/inbox</c> no emissor (design.md da change
/// servicos-token-emissor, D5; convenção 8): sobre o host construído, sem rede. O segredo
/// precisa estar presente, ser base64 e ter pelo menos 32 bytes decodificado, e ser distinto,
/// por bytes, da <c>Inbox:CredentialEncryptionKey</c> (decisão do dono, 03/11/2026): ele
/// trafega na rede interna e vive em dois contêineres, e a chave decifra as credenciais dos
/// canais. Nenhuma mensagem ecoa o valor.
/// <para>
/// O apps/connectors confere o seu em
/// apps/connectors/src/Buteco.Connectors/Auth/ServiceClientConfigurationValidation.cs, sem a
/// comparação, porque lá não há chave de cifra.
/// </para>
/// </summary>
public static class ServiceClientConfigurationValidation
{
    public const int MinimumBytes = 32;

    private const string CredentialEncryptionKeyName = "Inbox:CredentialEncryptionKey";

    public static void ValidateServiceClientConfiguration(this IHost host)
    {
        var secret = Decode(host.Services.GetRequiredService<IOptions<ServiceClientOptions>>().Value.Secret);

        // A chave de cifra tem a própria checagem no AesGcmChannelCredentialCipher; aqui só
        // a comparação, quando ela decodifica.
        if (TryDecodeBase64(host.Services.GetRequiredService<IOptions<InboxCryptoOptions>>().Value.CredentialEncryptionKey) is { } key
            && secret.AsSpan().SequenceEqual(key))
        {
            throw new InvalidOperationException(
                $"{ServiceClientOptions.SecretConfigurationName} é igual a {CredentialEncryptionKeyName}: o segredo do serviço "
              + "precisa ser distinto da chave de cifra das credenciais. Gere outro com `openssl rand -base64 32` e use o mesmo "
              + "valor em Identity:ServiceClients:Inbox:Secret no apps/api.");
        }
    }

    internal static byte[] Decode(string? configured)
    {
        const string name = ServiceClientOptions.SecretConfigurationName;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{name} não configurado. É o segredo do cliente {ServiceClientOptions.ClientId} no emissor do apps/api, "
              + "o mesmo valor de Identity:ServiceClients:Inbox:Secret lá (gerado com `openssl rand -base64 32`).");
        }

        var bytes = TryDecodeBase64(configured)
            ?? throw new InvalidOperationException($"{name} não é base64 válido. Gere um com `openssl rand -base64 32`.");

        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"{name} tem {bytes.Length} bytes; o mínimo é {MinimumBytes}. Gere um com `openssl rand -base64 32`.");
        }

        return bytes;
    }

    private static byte[]? TryDecodeBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
