using System.Text.RegularExpressions;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// Os tipos de provedor federado (design.md da change provedores-federados-google, D2). Na M3 só
/// existe o Google, e tudo o que o tipo fixa (issuer, nome, escopos, <c>prompt</c>) está aqui, sem
/// campo de configuração.
/// </summary>
public static class FederatedProviderTypes
{
    public const string Google = "google";

    /// <summary>O issuer fixo do tipo Google, com a barra final, como o WebIntegration o declara.</summary>
    public static readonly Uri GoogleIssuer = new("https://accounts.google.com/");

    public const string GoogleName = "Google";

    public static readonly IReadOnlyList<string> GoogleScopes = ["email", "profile"];

    public const string GooglePrompt = "select_account";

    public static IReadOnlyList<string> All { get; } = [Google];

    public static Uri IssuerOf(string type) => type switch
    {
        Google => GoogleIssuer,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de provedor federado desconhecido."),
    };

    public static string NameOf(string type) => type switch
    {
        Google => GoogleName,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de provedor federado desconhecido."),
    };
}

/// <summary>
/// Uma entrada de <c>Identity:FederatedProviders</c>, já lida. O segredo nunca sai em
/// <see cref="ToString"/>: o record padrão o imprimiria.
/// </summary>
public sealed record FederatedProvider(string Key, string Type, string ClientId, string ClientSecret, string? HostedDomain)
{
    public Uri Issuer => FederatedProviderTypes.IssuerOf(Type);

    public string Name => FederatedProviderTypes.NameOf(Type);

    public override string ToString() => $"FederatedProvider {{ Key = {Key}, Type = {Type} }}";
}

/// <summary>
/// Os provedores configurados (D2), lidos uma vez no registro, porque as registrações do cliente
/// OIDC precisam deles antes do <c>Build()</c>. <b>A leitura não lança</b>: o migration bundle roda o
/// <c>Program.cs</c> até o <c>Build()</c>, e quem reprova a configuração é
/// <see cref="FederatedProvidersStartupValidation"/>, depois dele. Por isso só entram aqui as entradas
/// completas e de tipo conhecido; as outras só existem para a checagem, que as lê da configuração.
/// </summary>
public sealed partial class FederatedProviderCatalog
{
    public const string SectionName = "Identity:FederatedProviders";

    public FederatedProviderCatalog(IEnumerable<FederatedProvider> providers)
    {
        Providers = providers.OrderBy(provider => provider.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Na ordem da chave.</summary>
    public IReadOnlyList<FederatedProvider> Providers { get; }

    public FederatedProvider? Find(string key) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// O provedor configurado com o issuer, ou nulo. Com a regra 6 do boot, há no máximo um. O
    /// issuer gravado no vínculo é o fixo do tipo, sempre com a barra final.
    /// </summary>
    public FederatedProvider? FindByIssuer(string issuer) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Issuer.AbsoluteUri, issuer, StringComparison.Ordinal));

    public static FederatedProviderCatalog Read(IConfiguration configuration)
    {
        var providers = new List<FederatedProvider>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var type = entry["Type"];
            var clientId = entry["ClientId"];
            var clientSecret = entry["ClientSecret"];
            if (!KeyPattern().IsMatch(entry.Key) ||
                type is null || !FederatedProviderTypes.All.Contains(type, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                continue;
            }

            var hostedDomain = entry["HostedDomain"];
            providers.Add(new FederatedProvider(entry.Key, type, clientId.Trim(), clientSecret,
                string.IsNullOrWhiteSpace(hostedDomain) ? null : hostedDomain.Trim().ToLowerInvariant()));
        }

        return new FederatedProviderCatalog(providers);
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$")]
    public static partial Regex KeyPattern();
}
