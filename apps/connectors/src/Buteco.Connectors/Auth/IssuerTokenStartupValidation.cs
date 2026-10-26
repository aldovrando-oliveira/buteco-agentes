using Buteco.Connectors.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerTokenStartupValidation.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Checagens de boot da validação do token do emissor (change
/// validacao-jwks-inbox-connectors, D7; convenção 8). Chamadas do <c>Program.cs</c>
/// sobre o host construído, sem chamada de rede, ao lado de
/// <c>ValidateApiConfiguration</c>. Aqui o <c>Api:BaseUrl</c> é opcional (o ciclo de
/// sincronização só liga com ele), então o issuer ligado sem ele derruba o boot.
/// <para>
/// <b>O registro no <c>Program.cs</c> é provado por teste:</b>
/// <c>IssuerTokenStartupValidationTests.InvalidIssuer_FailsTheRealApplicationStartup</c>
/// sobe a aplicação real e reprova se a chamada sair de lá.
/// </para>
/// </summary>
public static class IssuerTokenStartupValidation
{
    public static void ValidateIssuerTokenConfiguration(this IHost host)
    {
        var options = host.Services.GetRequiredService<IOptions<IssuerTokenOptions>>().Value;
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IssuerTokenStartupValidation).FullName!);

        if (!options.Enabled)
        {
            logger.LogWarning(
                "Validação do token do emissor desligada: Identity:Issuer não configurado. "
              + "Só o token HMAC (Auth:TokenSigningKey) é aceito.");
            return;
        }

        if (!IsAbsoluteHttpUrl(options.Issuer!.Trim()))
        {
            throw new InvalidOperationException(
                $"Identity:Issuer está configurado como '{options.Issuer}', e precisa ser uma url absoluta http ou https, "
              + "sem query nem fragmento: é o issuer público do emissor do apps/api, o mesmo valor de PublicUrl:BaseUrl lá. "
              + "Para desligar a validação do token do emissor, deixe Identity:Issuer vazio.");
        }

        var apiBaseUrl = host.Services.GetRequiredService<IOptions<ApiOptions>>().Value.BaseUrl;
        // Sem ecoar o valor, como ValidateApiConfiguration do apps/connectors: o endereço
        // interno pode carregar credencial.
        if (string.IsNullOrWhiteSpace(apiBaseUrl) || !IsAbsoluteHttpUrl(apiBaseUrl))
        {
            throw new InvalidOperationException(
                "Identity:Issuer está configurado, e Api:BaseUrl está ausente ou não é uma url absoluta http ou https: "
              + "é pelo endereço interno do apps/api que as chaves do emissor são obtidas. "
              + "Configure Api:BaseUrl, ou deixe Identity:Issuer vazio para desligar a validação do token do emissor.");
        }

        logger.LogInformation(
            "Validação do token do emissor ligada: issuer {Issuer}, audience {Audience}, chaves pelo Api:BaseUrl.",
            options.NormalizedIssuer,
            IssuerTokenAudience.Value);
    }

    public static void ValidateIssuerClientSubjects(this IHost host) =>
        ValidateIssuerClientSubjects(IssuerClientSubjects.Table, [.. ConnectorsSubjectAuthorizationHandler.SubjectRoutes.Keys]);

    // Pura: todo subject de destino da tabela de clientes existe na tabela de subjects
    // do app. O sentido inverso não vale: o HMAC tem subjects que nenhum cliente usa.
    public static void ValidateIssuerClientSubjects(
        IReadOnlyDictionary<string, string> clientSubjects, IReadOnlyCollection<string> knownSubjects)
    {
        var invalid = clientSubjects
            .Where(entry => !knownSubjects.Contains(entry.Value, StringComparer.Ordinal))
            .Select(entry => $"{entry.Key} → {entry.Value}")
            .ToList();

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                "IssuerClientSubjects aponta para subject que a tabela de subjects deste app não conhece: "
              + string.Join(", ", invalid)
              + $". Subjects conhecidos: {string.Join(", ", knownSubjects)}.");
        }
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
