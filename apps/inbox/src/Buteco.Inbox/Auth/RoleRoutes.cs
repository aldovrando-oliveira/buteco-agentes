namespace Buteco.Inbox.Auth;

// A matriz de papéis por rota do token do painel no apps/inbox (design.md da change
// papeis-no-token, D1 e D2; spec route-roles), casada por método e RoutePattern.RawText, com a
// barra final que o MapGroup gera para MapGet("/"). O apps/inbox não tem rota de subject de
// serviço: toda rota autenticada é do painel, e o papel decide. Conferida no boot nos três
// sentidos (RouteDeclarationValidation, neste app).
public static class RoleRoutes
{
    private static readonly string[] All = [PanelRoles.Admin, PanelRoles.Operator, PanelRoles.Reader];
    private static readonly string[] Operate = [PanelRoles.Admin, PanelRoles.Operator];

    public static IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<string>> Table { get; } =
        new Dictionary<(string Method, string Pattern), IReadOnlyList<string>>
        {
            // Canais (matriz: "agentes, MCP, conhecimento, canais" — Admin e Operador gerem, o
            // Leitor lê, sem credenciais, que nenhuma resposta devolve).
            [("GET", "/channels/")] = All,
            [("GET", "/channels/{id:guid}")] = All,
            [("POST", "/channels/")] = Operate,
            [("PUT", "/channels/{id:guid}")] = Operate,
            [("POST", "/channels/{id:guid}/activate")] = Operate,
            [("POST", "/channels/{id:guid}/deactivate")] = Operate,

            // Histórico de conversas (matriz: Admin e Operador veem; o Leitor não). Os contatos são
            // quem conversa, com nome e telefone: entram com o histórico (Q8c).
            [("GET", "/channels/{channelId:guid}/sessions")] = Operate,
            [("GET", "/sessions/{sessionId:guid}/messages")] = Operate,
            [("GET", "/contacts/")] = Operate,
            [("GET", "/contacts/{id:guid}/sessions")] = Operate,

            // Os resumos do Insights: contagens por período, sem conteúdo de conversa (Q8b).
            [("GET", "/sessions/summary")] = All,
            [("GET", "/messages/summary")] = All,
        };
}
