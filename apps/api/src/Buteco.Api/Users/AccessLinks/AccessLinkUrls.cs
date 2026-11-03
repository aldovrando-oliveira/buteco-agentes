using Buteco.Api.Identity;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// O endereço do link (design.md da change convites-redefinicao-senha, D3 e D4):
/// <c>&lt;base do painel&gt;/convite#&lt;token&gt;</c> e
/// <c>&lt;base do painel&gt;/redefinir-senha#&lt;token&gt;</c>. O token vai no fragmento, que o
/// navegador não envia ao servidor: fica fora do log de acesso do nginx (o <c>$request</c> leva
/// caminho e query), do proxy de fora e dos robôs de pré-visualização. O painel o lê de
/// <c>location.hash</c> e o manda no corpo de um <c>POST</c>.
/// </summary>
public static class AccessLinkUrls
{
    public const string InvitationPath = "/convite";
    public const string PasswordResetPath = "/redefinir-senha";

    public static string For(AccessLinkPurpose purpose, Uri panelBase, string token)
    {
        var path = purpose switch
        {
            AccessLinkPurpose.Invitation => InvitationPath,
            AccessLinkPurpose.PasswordReset => PasswordResetPath,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null),
        };

        return $"{panelBase.GetLeftPart(UriPartial.Path).TrimEnd('/')}{path}#{token}";
    }
}
