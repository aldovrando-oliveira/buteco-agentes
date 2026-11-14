using System.Text.RegularExpressions;
using Buteco.Api.Identity.Clients;
using Microsoft.Extensions.Configuration.Json;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// A checagem de boot de <c>Identity:FederatedProviders</c> (convenção 8; design.md da change
/// provedores-federados-google, D3), depois do <c>Build()</c>, sem banco e sem rede: alcançar o
/// discovery do provedor não é condição de boot. A lista é fechada, e cada inconsistência tem a sua
/// mensagem, que cita a chave e o campo e nunca o valor do segredo. Seção ausente ou vazia é válida.
/// </summary>
public static partial class FederatedProvidersStartupValidation
{
    private static readonly string[] KnownFields = ["Type", "ClientId", "ClientSecret", "HostedDomain"];

    /// <summary>As chaves e os segredos do <c>apps/api</c> de que o segredo do provedor precisa ser distinto (regra 10).</summary>
    private static readonly string[] OwnSecrets =
    [
        "Identity:SigningKeyEncryptionKey",
        "Identity:TwoFactorEncryptionKey",
        "Mcp:CredentialEncryptionKey",
        ServiceClientSecret.InboxConfigurationName,
        ServiceClientSecret.ConnectorsConfigurationName,
    ];

    public static void ValidateFederatedProvidersConfiguration(this IHost host)
    {
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Validate(configuration);
    }

    public static void Validate(IConfiguration configuration)
    {
        var section = configuration.GetSection(FederatedProviderCatalog.SectionName);
        var issuers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in section.GetChildren().OrderBy(child => child.Key, StringComparer.Ordinal))
        {
            var key = entry.Key;
            var prefix = $"{FederatedProviderCatalog.SectionName}:{key}";

            // 1. A chave vai nas rotas e no motivo de recusa.
            if (!FederatedProviderCatalog.KeyPattern().IsMatch(key))
            {
                throw new InvalidOperationException(
                    $"{prefix}: a chave precisa ter de 2 a 32 caracteres, minúsculas, dígitos e hífen, começando por letra (por exemplo corporativo).");
            }

            // 8. Campo fora da lista: o binder ignoraria um "ClientID" em silêncio.
            foreach (var field in entry.GetChildren())
            {
                if (!KnownFields.Contains(field.Key, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{prefix}:{field.Key} desconhecido: os campos do tipo google são Type, ClientId, ClientSecret e HostedDomain.");
                }
            }

            // 2 e 3.
            var type = entry["Type"];
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new InvalidOperationException($"{prefix}:Type não configurado: o tipo é obrigatório (na M3, só google).");
            }

            if (!FederatedProviderTypes.All.Contains(type, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{prefix}:Type '{type}' desconhecido: os tipos aceitos são {string.Join(", ", FederatedProviderTypes.All)}.");
            }

            // 4.
            var clientId = entry["ClientId"];
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Trim().Any(char.IsWhiteSpace))
            {
                throw new InvalidOperationException($"{prefix}:ClientId não configurado: use o ID do cliente OAuth criado no provedor.");
            }

            // 5.
            var clientSecret = entry["ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    $"{prefix}:ClientSecret não configurado: defina a variável Identity__FederatedProviders__{key}__ClientSecret, nunca no arquivo de configuração.");
            }

            // 7. O valor efetivo é o do último provedor de configuração que o tem.
            if (EffectiveSourceIsJson(configuration, $"{prefix}:ClientSecret"))
            {
                throw new InvalidOperationException(
                    $"{prefix}:ClientSecret veio de um arquivo de configuração: o segredo só é aceito por variável de ambiente.");
            }

            // 10. Um segredo que vive no contêiner não pode ser uma chave de cifra nem outro segredo do app.
            foreach (var own in OwnSecrets)
            {
                if (string.Equals(configuration[own], clientSecret, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{prefix}:ClientSecret igual a {own}: o segredo do provedor precisa ser distinto das chaves e dos segredos do apps/api.");
                }
            }

            // 9.
            var hostedDomain = entry["HostedDomain"];
            if (hostedDomain is not null && !DomainPattern().IsMatch(hostedDomain))
            {
                throw new InvalidOperationException(
                    $"{prefix}:HostedDomain '{hostedDomain}' inválido: use só o domínio, sem @ (por exemplo empresa.com).");
            }

            // 6. O vínculo é pelo par (issuer, sub).
            var issuer = FederatedProviderTypes.IssuerOf(type).AbsoluteUri;
            if (issuers.TryGetValue(issuer, out var other))
            {
                throw new InvalidOperationException(
                    $"{FederatedProviderCatalog.SectionName}: '{other}' e '{key}' usam o mesmo issuer ({issuer}): o vínculo é pelo par (issuer, sub), e dois provedores com o mesmo issuer o tornariam ambíguo.");
            }

            issuers[issuer] = key;
        }
    }

    private static bool EffectiveSourceIsJson(IConfiguration configuration, string path)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return false;
        }

        foreach (var provider in root.Providers.Reverse())
        {
            if (provider.TryGet(path, out _))
            {
                return provider is JsonConfigurationProvider or JsonStreamConfigurationProvider;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainPattern();
}
