namespace Buteco.Api.Identity.Clients;

/// <summary>
/// O segredo de um cliente de serviço na configuração (design.md da change
/// clientes-servico-emissor, D2). Não é chave de cifra: vai ao OpenIddict como texto, e o
/// serviço o manda como texto. Os 32 bytes em base64 são só a régua de entropia, a mesma do
/// <c>openssl rand -base64 32</c> das outras chaves da casa. Nenhuma mensagem ecoa o valor.
/// </summary>
public static class ServiceClientSecret
{
    public const string InboxConfigurationName = "Identity:ServiceClients:Inbox:Secret";
    public const string ConnectorsConfigurationName = "Identity:ServiceClients:Connectors:Secret";

    public const int MinimumBytes = 32;

    /// <summary>Lê e confere o segredo; devolve os bytes decodificados, para a checagem de distinção.</summary>
    public static byte[] Decode(string configurationName, string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{configurationName} não configurado. Gere um segredo com `openssl rand -base64 32`, um por serviço e " +
                "distinto das outras chaves, e configure via variável de ambiente.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{configurationName} não é base64 válido. Gere um com `openssl rand -base64 32`.");
        }

        if (bytes.Length < MinimumBytes)
        {
            throw new InvalidOperationException(
                $"{configurationName} tem {bytes.Length} bytes; o mínimo é {MinimumBytes}. Gere um com `openssl rand -base64 32`.");
        }

        return bytes;
    }
}
