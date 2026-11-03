using Buteco.Connectors.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Auth;

/// <summary>
/// Checagem de boot do segredo do <c>apps/connectors</c> no emissor (design.md da change
/// servicos-token-emissor, D5; convenção 8): sobre o host construído, sem rede, e só com
/// <c>Api:BaseUrl</c> presente, que é quando o ciclo de sincronização chama o
/// <c>apps/api</c>. O segredo precisa estar presente, ser base64 e ter pelo menos 32 bytes
/// decodificado. Nenhuma mensagem ecoa o valor.
/// <para>
/// O apps/inbox confere o seu em
/// apps/inbox/src/Buteco.Inbox/Auth/ServiceClientConfigurationValidation.cs, e lá também o
/// compara com a chave de cifra das credenciais; aqui não há chave de cifra.
/// </para>
/// </summary>
public static class ServiceClientConfigurationValidation
{
    public const int MinimumBytes = 32;

    public static void ValidateServiceClientConfiguration(this IHost host)
    {
        if (!host.Services.GetRequiredService<IOptions<ApiOptions>>().Value.IsConfigured)
        {
            return;
        }

        Decode(host.Services.GetRequiredService<IOptions<ServiceClientOptions>>().Value.Secret);
    }

    internal static byte[] Decode(string? configured)
    {
        const string name = ServiceClientOptions.SecretConfigurationName;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{name} não configurado, e Api:BaseUrl está: é o segredo do cliente {ServiceClientOptions.ClientId} no emissor "
              + "do apps/api, o mesmo valor de Identity:ServiceClients:Connectors:Secret lá (gerado com `openssl rand -base64 32`).");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{name} não é base64 válido. Gere um com `openssl rand -base64 32`.");
        }

        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"{name} tem {bytes.Length} bytes; o mínimo é {MinimumBytes}. Gere um com `openssl rand -base64 32`.");
        }

        return bytes;
    }
}
