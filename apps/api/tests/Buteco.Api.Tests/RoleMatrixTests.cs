using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests;

/// <summary>
/// "Guarda da matriz por app, sobre a tabela real" (spec <c>route-roles</c>; design.md da change
/// papeis-no-token, D11): cada entrada da <see cref="RoleRoutes"/> de produção, chamada por um usuário
/// real de cada papel (as rotas de <c>CurrentUser</c> exigem pessoa), com o token do fluxo do emissor.
///
/// <para>
/// <b>A matriz esperada é escrita aqui, à parte da de produção</b>, a partir da tabela da spec
/// (A = Admin, O = Operador, L = Leitor). Um guarda que lesse a expectativa da própria
/// <see cref="RoleRoutes"/> seria circular: o defeito "papel a mais numa entrada" mudaria a tabela e a
/// expectativa juntas, e nada reprovaria (medido na 3.3 do tasks.md). A tabela real é conferida contra
/// esta, entrada por entrada, e o comportamento de cada rota, contra esta.
/// </para>
///
/// <para>
/// <b>Recusa pela autorização × resposta da rota.</b> O <c>403</c> do handler de papel e o <c>401</c>
/// de token saem com o corpo vazio; a própria rota recusa com <c>application/problem+json</c> (a senha
/// errada do <c>POST /account/password</c>, o id que não existe). O caso separa os dois pelo corpo: o
/// papel fora da entrada tem de receber o <c>403</c> vazio, e o papel dentro, qualquer coisa que não
/// seja um dos dois vazios. Os ids vão aleatórios, nunca o de quem chama, para nenhuma rota recusar
/// "a si mesmo".
/// </para>
/// </summary>
public class RoleMatrixTests(RoleMatrixTests.Fixture factory) : IClassFixture<RoleMatrixTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<UserRole, SignedInUser> _users = [];

        public async Task<SignedInUser> UserAsync(UserRole role)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_users.TryGetValue(role, out var user))
                {
                    user = await SignedInUser.CreateAsync(this, $"matriz-{UserRoleText.Write(role)}", role: role);
                    _users[role] = user;
                }

                return user;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static readonly Dictionary<(string Method, string Pattern), string> Expected = new()
    {
        [("GET", "/agents/")] = "A O L",
        [("GET", "/agents/{id:guid}")] = "A O L",
        [("POST", "/agents/")] = "A O",
        [("PUT", "/agents/{id:guid}")] = "A O",
        [("POST", "/agents/{id:guid}/activate")] = "A O",
        [("POST", "/agents/{id:guid}/deactivate")] = "A O",
        [("PUT", "/agents/{id:guid}/mcp-servers")] = "A O",
        [("PUT", "/agents/{id:guid}/delegations")] = "A O",
        [("PUT", "/agents/{id:guid}/knowledge-bases")] = "A O",
        [("GET", "/providers")] = "A O L",
        [("GET", "/mcp-servers/")] = "A O L",
        [("GET", "/mcp-servers/{id:guid}")] = "A O L",
        [("GET", "/mcp-servers/{id:guid}/tools")] = "A O L",
        [("POST", "/mcp-servers/")] = "A O",
        [("PUT", "/mcp-servers/{id:guid}")] = "A O",
        [("POST", "/mcp-servers/{id:guid}/activate")] = "A O",
        [("POST", "/mcp-servers/{id:guid}/deactivate")] = "A O",
        [("POST", "/mcp-servers/test")] = "A O",
        [("POST", "/mcp-servers/{id:guid}/test")] = "A O",
        [("GET", "/knowledge-bases/")] = "A O L",
        [("GET", "/knowledge-bases/indexing-summary")] = "A O L",
        [("GET", "/knowledge-bases/{id:guid}")] = "A O L",
        [("POST", "/knowledge-bases/")] = "A O",
        [("PUT", "/knowledge-bases/{id:guid}")] = "A O",
        [("POST", "/knowledge-bases/{id:guid}/activate")] = "A O",
        [("POST", "/knowledge-bases/{id:guid}/deactivate")] = "A O",
        [("DELETE", "/knowledge-bases/{id:guid}")] = "A O",
        [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/documents/")] = "A O L",
        [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = "A O L",
        [("GET", "/knowledge-bases/{knowledgeBaseId:guid}/document-events")] = "A O L",
        [("POST", "/knowledge-bases/{knowledgeBaseId:guid}/documents/")] = "A O",
        [("PUT", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = "A O",
        [("POST", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}/reindex")] = "A O",
        [("DELETE", "/knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}")] = "A O",
        [("GET", "/knowledge-index/diagnostics")] = "A O L",
        [("GET", "/insights/system")] = "A O L",
        [("GET", "/insights/agents/{id:guid}")] = "A O L",
        [("GET", "/users/")] = "A",
        [("GET", "/users/{id:guid}")] = "A",
        [("PUT", "/users/{id:guid}")] = "A",
        [("POST", "/users/{id:guid}/deactivate")] = "A",
        [("POST", "/users/{id:guid}/activate")] = "A",
        [("POST", "/users/{id:guid}/sessions/revoke")] = "A",
        [("PUT", "/users/{id:guid}/role")] = "A",
        [("POST", "/users/{id:guid}/two-factor/reset")] = "A",
        [("POST", "/users/invitations")] = "A",
        [("POST", "/users/{id:guid}/invitation/renew")] = "A",
        [("POST", "/users/{id:guid}/invitation/cancel")] = "A",
        [("POST", "/users/{id:guid}/password-reset")] = "A",
        [("GET", "/identity")] = "A",
        [("GET", "/account/")] = "A O L",
        [("POST", "/account/password")] = "A O L",
        [("GET", "/account/sessions/")] = "A O L",
        [("POST", "/account/sessions/{id}/revoke")] = "A O L",
        [("POST", "/account/sessions/revoke-others")] = "A O L",
        [("GET", "/account/two-factor/")] = "A O L",
        [("POST", "/account/two-factor/recovery-codes")] = "A O L",
        // Aplicações externas (change aplicacoes-externas-credencial, D3): só o Admin, inclusive ler.
        [("GET", "/applications/")] = "A",
        [("POST", "/applications/")] = "A",
        [("GET", "/applications/{id:guid}")] = "A",
        [("PUT", "/applications/{id:guid}")] = "A",
        [("POST", "/applications/{id:guid}/deactivate")] = "A",
        [("POST", "/applications/{id:guid}/activate")] = "A",
        [("POST", "/applications/{id:guid}/secrets")] = "A",
        [("POST", "/applications/{id:guid}/secrets/{secretId:guid}/revoke")] = "A",
    };

    private static bool ExpectedAllows(string method, string pattern, UserRole role) =>
        Expected[(method, pattern)].Split(' ').Contains(role switch
        {
            UserRole.Admin => "A",
            UserRole.Operator => "O",
            UserRole.Reader => "L",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        });

    public static TheoryData<string, string, UserRole> Cases()
    {
        var data = new TheoryData<string, string, UserRole>();
        foreach (var (method, pattern) in Expected.Keys.OrderBy(key => key.Pattern, StringComparer.Ordinal).ThenBy(key => key.Method, StringComparer.Ordinal))
        {
            foreach (var role in Enum.GetValues<UserRole>())
            {
                data.Add(method, pattern, role);
            }
        }

        return data;
    }

    [Fact]
    public void TheProductionTable_IsTheExpectedMatrix()
    {
        Assert.NotEmpty(Expected);
        Assert.Equal(Expected.Keys.OrderBy(key => key.ToString()), RoleRoutes.Table.Keys.OrderBy(key => key.ToString()));
        foreach (var (route, roles) in RoleRoutes.Table)
        {
            foreach (var role in Enum.GetValues<UserRole>())
            {
                Assert.True(
                    roles.Contains(role) == ExpectedAllows(route.Method, route.Pattern, role),
                    $"{route.Method} {route.Pattern}: o papel {role} diverge da matriz da spec");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachRoute_ForEachRole_FollowsTheTable(string method, string pattern, UserRole role)
    {
        var caller = await factory.UserAsync(role);
        var response = await SendAsync(caller, method, pattern);
        var deniedByAuthorization = await IsEmptyAsync(response, HttpStatusCode.Forbidden);
        var tokenRefused = await IsEmptyAsync(response, HttpStatusCode.Unauthorized);

        Assert.False(tokenRefused, $"{method} {pattern} ({role}): 401 de token, sem corpo");
        if (ExpectedAllows(method, pattern, role))
        {
            Assert.False(deniedByAuthorization, $"{method} {pattern}: o papel {role} está na entrada e recebeu o 403 da autorização");
        }
        else
        {
            Assert.True(deniedByAuthorization, $"{method} {pattern}: o papel {role} não está na entrada e recebeu {(int)response.StatusCode}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(SignedInUser caller, string method, string pattern)
    {
        var path = pattern
            .Replace("{id:guid}", Guid.NewGuid().ToString(), StringComparison.Ordinal)
            .Replace("{knowledgeBaseId:guid}", Guid.NewGuid().ToString(), StringComparison.Ordinal)
            .Replace("{secretId:guid}", Guid.NewGuid().ToString(), StringComparison.Ordinal)
            .Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);
        if (path.Contains('{', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Parâmetro de rota sem valor no guarda da matriz: {pattern}");
        }

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await SignedInUser.Anonymous(factory).SendAsync(request);
    }

    private static async Task<bool> IsEmptyAsync(HttpResponseMessage response, HttpStatusCode status) =>
        response.StatusCode == status && (await response.Content.ReadAsByteArrayAsync()).Length == 0;
}
