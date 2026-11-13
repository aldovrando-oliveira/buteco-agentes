using Buteco.Api.Identity.Federated;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Federated;

/// <summary>Uma conta conectada, como as rotas de usuários e de Minha conta a mostram (sem o <c>sub</c>).</summary>
public sealed record ConnectedAccount(string? Chave, string Tipo, string? Email, DateTimeOffset ConectadaEm, DateTimeOffset? UltimoUso, bool Disponivel);

/// <summary>
/// As formas de entrada de um usuário (design.md da change provedores-federados-google, D6 e D9): a senha
/// gravada e cada vínculo com um provedor presente na configuração. O vínculo cujo issuer saiu da
/// configuração fica gravado e aparece nas contas conectadas, mas não entra e não conta.
/// </summary>
public static class FederatedSignInForms
{
    public const string UnknownType = "desconhecido";

    /// <summary>
    /// O <c>formasDeEntrada</c> de <c>/users</c> e <c>/account</c>, no formato de sempre (strings):
    /// <c>"senha"</c>, depois o tipo de cada provedor disponível com vínculo, sem repetir.
    /// </summary>
    public static IReadOnlyList<string> Of(bool hasPassword, IEnumerable<UserFederatedIdentity> links, FederatedProviderCatalog catalog)
    {
        var forms = new List<string>();
        if (hasPassword)
        {
            forms.Add(Management.UserSummary.PasswordSignIn);
        }

        foreach (var type in links.Select(link => catalog.FindByIssuer(link.Issuer)?.Type).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            forms.Add(type);
        }

        return forms;
    }

    public static IReadOnlyList<ConnectedAccount> Connected(IEnumerable<UserFederatedIdentity> links, FederatedProviderCatalog catalog) =>
        links.OrderBy(link => link.ConnectedAt).ThenBy(link => link.Id).Select(link =>
        {
            var provider = catalog.FindByIssuer(link.Issuer);
            var type = provider?.Type ?? (link.Issuer == FederatedProviderTypes.GoogleIssuer.AbsoluteUri ? FederatedProviderTypes.Google : UnknownType);
            return new ConnectedAccount(provider?.Key, type, link.Email, link.ConnectedAt, link.LastUsedAt, provider is not null);
        }).ToList();

    /// <summary>Quantas formas de entrada a pessoa tem: a senha mais os vínculos de provedor disponível.</summary>
    public static int Count(bool hasPassword, IEnumerable<UserFederatedIdentity> links, FederatedProviderCatalog catalog) =>
        (hasPassword ? 1 : 0) + links.Count(link => catalog.FindByIssuer(link.Issuer) is not null);

    public static Task<List<UserFederatedIdentity>> LinksOfAsync(AppDbContext dbContext, Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserFederatedIdentities.AsNoTracking().Where(link => link.UserId == userId).ToListAsync(cancellationToken);
}
