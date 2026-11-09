namespace Buteco.Connectors.Auth;

// Os papéis do token do painel nas rotas do apps/connectors (design.md da change papeis-no-token,
// D1 e D2; decisão do dono de 23/10/2026): Admin e Operador acessam listar provedores, navegar
// pastas e "Sincronizar agora"; o Leitor recebe 403. A lista de rotas do subject operator na
// ConnectorsSubjectAuthorizationHandler.SubjectRoutes SAI DESTA TABELA, e não é escrita duas vezes.
// A rota de descrever pasta é só do service:api e não está aqui.
public static class RoleRoutes
{
    private static readonly string[] Operate = [PanelRoles.Admin, PanelRoles.Operator];

    public static IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<string>> Table { get; } =
        new Dictionary<(string Method, string Pattern), IReadOnlyList<string>>
        {
            [("GET", "/connectors/providers")] = Operate,
            [("GET", "/connectors/providers/{providerKey}/folders")] = Operate,
            [("POST", Endpoints.SyncEndpoints.SyncNowPattern)] = Operate,
        };
}
