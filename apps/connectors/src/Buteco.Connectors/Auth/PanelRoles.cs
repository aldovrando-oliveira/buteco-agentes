namespace Buteco.Connectors.Auth;

// Os papéis do token do painel (design.md da change papeis-no-token, D3), no texto do claim
// role que o emissor do apps/api grava (UserRoleText, lá). Copiado, sem libs/, como a tabela de
// clientes: o mesmo arquivo existe em apps/inbox, e o acordo de tests/ prova o texto contra o
// emissor real. O token do painel sem um destes três recebe 401 (IssuerTokenAuthentication).
public static class PanelRoles
{
    public const string Claim = "role";

    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string Reader = "reader";

    public static bool IsKnown(string? role) => role is Admin or Operator or Reader;
}
