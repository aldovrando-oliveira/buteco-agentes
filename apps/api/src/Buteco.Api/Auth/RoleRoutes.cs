using Buteco.Api.Users;

namespace Buteco.Api.Auth;

/// <summary>
/// A matriz de papéis por rota do token do painel (design.md da change papeis-no-token, D1 e D2;
/// spec <c>route-roles</c>), casada por método e <see cref="Microsoft.AspNetCore.Routing.Patterns.RoutePattern.RawText"/>,
/// com a barra final que o <c>MapGroup</c> gera para <c>MapGet("/")</c> e <c>MapGet("")</c>.
///
/// <para>
/// O subject <c>operator</c> (o token do painel) passa só nas rotas desta tabela cujo papel ele
/// traz; ele deixou de passar em tudo. As rotas de serviço (<c>POST /agents/{id}/a2a</c> e
/// <c>/sync/knowledge-bases/*</c>) <b>não estão aqui</b>, de propósito: são só dos subjects de
/// serviço (<see cref="ServiceScopeAuthorizationHandler.ServiceRoutes"/>), e o painel, que não as
/// chama, recebe <c>403</c> nelas (Q8f). Toda rota autenticada está numa das duas tabelas, e toda
/// entrada tem rota, conferido no boot (<see cref="RouteDeclarationValidation"/>).
/// </para>
///
/// <para>
/// Nas rotas de Admin que identificam a pessoa pela <see cref="CurrentUser"/>, a rota confere
/// também o papel <b>gravado</b> (<c>CurrentUser.FindAdminAsync</c>): o rebaixamento vale
/// ali na requisição seguinte, e no resto em até 15 min 30 s, o prazo do access token (D3, Q1).
/// </para>
/// </summary>
public static class RoleRoutes
{
    private static readonly UserRole[] All = [UserRole.Admin, UserRole.Operator, UserRole.Reader];
    private static readonly UserRole[] Operate = [UserRole.Admin, UserRole.Operator];
    private static readonly UserRole[] AdminOnly = [UserRole.Admin];

    public static IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<UserRole>> Table { get; } =
        new Dictionary<(string Method, string Pattern), IReadOnlyList<UserRole>>
        {
            // Agentes (matriz: "agentes, MCP, conhecimento, canais" — Admin e Operador gerem, o
            // Leitor lê). O catálogo de provedores é a leitura do cadastro de agente.
            [("GET", "/agents/")] = All,
            [("GET", "/agents/{id:guid}")] = All,
            [("POST", "/agents/")] = Operate,
            [("PUT", "/agents/{id:guid}")] = Operate,
            [("POST", "/agents/{id:guid}/activate")] = Operate,
            [("POST", "/agents/{id:guid}/deactivate")] = Operate,
            [("PUT", "/agents/{id:guid}/mcp-servers")] = Operate,
            [("PUT", "/agents/{id:guid}/delegations")] = Operate,
            [("PUT", "/agents/{id:guid}/knowledge-bases")] = Operate,
            [("GET", "/providers")] = All,

            // MCP. Listar as ferramentas usa a credencial gravada sem devolvê-la, e é leitura; os
            // dois "testar" são ação com chamada de saída, e a credencial do não salvo vem no
            // corpo (Q8d).
            [("GET", "/mcp-servers/")] = All,
            [("GET", "/mcp-servers/{id:guid}")] = All,
            [("GET", "/mcp-servers/{id:guid}/tools")] = All,
            [("POST", "/mcp-servers/")] = Operate,
            [("PUT", "/mcp-servers/{id:guid}")] = Operate,
            [("POST", "/mcp-servers/{id:guid}/activate")] = Operate,
            [("POST", "/mcp-servers/{id:guid}/deactivate")] = Operate,
            [("POST", "/mcp-servers/test")] = Operate,
            [("POST", "/mcp-servers/{id:guid}/test")] = Operate,

            // Conhecimento.
            [("GET", "/knowledge-bases/")] = All,
            [("GET", "/knowledge-bases/indexing-summary")] = All,
            [("GET", "/knowledge-bases/{id:guid}")] = All,
            [("POST", "/knowledge-bases/")] = Operate,
            [("PUT", "/knowledge-bases/{id:guid}")] = Operate,
            [("POST", "/knowledge-bases/{id:guid}/activate")] = Operate,
            [("POST", "/knowledge-bases/{id:guid}/deactivate")] = Operate,
            [("DELETE", "/knowledge-bases/{id:guid}")] = Operate,
            [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/documents/")] = All,
            [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = All,
            [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/document-events")] = All,
            [("POST", "/knowledge-bases/{knowledgeBaseId:guid}/documents/")] = Operate,
            [("PUT", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = Operate,
            [("POST", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}/reindex")] = Operate,
            [("DELETE", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = Operate,
            [("GET", "/knowledge-index/diagnostics")] = All,

            // Insights: números agregados, sem conversa (Q8a).
            [("GET", "/insights/system")] = All,
            [("GET", "/insights/agents/{id:guid}")] = All,

            // Usuários e identidade (matriz: só Admin). Estas rotas também leem o papel gravado.
            [("GET", "/users/")] = AdminOnly,
            [("GET", "/users/{id:guid}")] = AdminOnly,
            [("PUT", "/users/{id:guid}")] = AdminOnly,
            [("POST", "/users/{id:guid}/deactivate")] = AdminOnly,
            [("POST", "/users/{id:guid}/activate")] = AdminOnly,
            [("POST", "/users/{id:guid}/sessions/revoke")] = AdminOnly,
            [("PUT", "/users/{id:guid}/role")] = AdminOnly,
            [("POST", "/users/{id:guid}/two-factor/reset")] = AdminOnly,
            [("POST", "/users/invitations")] = AdminOnly,
            [("POST", "/users/{id:guid}/invitation/renew")] = AdminOnly,
            [("POST", "/users/{id:guid}/invitation/cancel")] = AdminOnly,
            [("POST", "/users/{id:guid}/password-reset")] = AdminOnly,
            [("GET", "/identity")] = AdminOnly,

            // Minha conta: a própria pessoa, todo papel (#219).
            [("GET", "/account/")] = All,
            [("POST", "/account/password")] = All,
            [("GET", "/account/sessions/")] = All,
            [("POST", "/account/sessions/{id}/revoke")] = All,
            [("POST", "/account/sessions/revoke-others")] = All,
            [("GET", "/account/two-factor/")] = All,
            [("POST", "/account/two-factor/recovery-codes")] = All,

            // Aplicações externas (design.md da change aplicacoes-externas-credencial, D3): só o
            // Admin, inclusive ler (#184). As rotas exigem também o papel gravado (CurrentUser).
            [("GET", "/applications/")] = AdminOnly,
            [("POST", "/applications/")] = AdminOnly,
            [("GET", "/applications/{id:guid}")] = AdminOnly,
            [("PUT", "/applications/{id:guid}")] = AdminOnly,
            [("POST", "/applications/{id:guid}/deactivate")] = AdminOnly,
            [("POST", "/applications/{id:guid}/activate")] = AdminOnly,
            [("POST", "/applications/{id:guid}/secrets")] = AdminOnly,
            [("POST", "/applications/{id:guid}/secrets/{secretId:guid}/revoke")] = AdminOnly,

            // Os agentes liberados para cada aplicação (agentes-por-aplicacao, D5): só o Admin, com o
            // papel gravado, como o resto de /applications. No agente, o card "Aplicações com acesso"
            // em duas rotas (Q8): a lista, com os nomes, só do Admin (e do papel gravado); a contagem,
            // só números, dos três papéis.
            [("GET", "/applications/{id:guid}/agents")] = AdminOnly,
            [("POST", "/applications/{id:guid}/agents")] = AdminOnly,
            [("PUT", "/applications/{id:guid}/agents/{agentId:guid}")] = AdminOnly,
            [("DELETE", "/applications/{id:guid}/agents/{agentId:guid}")] = AdminOnly,
            [("GET", "/agents/{id:guid}/applications")] = AdminOnly,
            [("GET", "/agents/{id:guid}/applications/count")] = All,
        };

    public static bool Allows(string method, string pattern, UserRole role) =>
        Table.TryGetValue((method.ToUpperInvariant(), pattern), out var roles) && roles.Contains(role);
}
