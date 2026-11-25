using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Sem provedor, nada muda para quem não usa o Google (change conectar-falha-minha-conta, #281, D0, P2; spec
/// federated-providers, "Provedores federados por configuração"; a promessa do docs/deployment.md). Desde a #281 o
/// cliente OIDC é registrado também sem provedor, sem registração: estes casos prendem o que a sonda da tarefa 0
/// mediu com o proxy morto e com a lista de caminhos comparada contra a imagem do origin/main.
/// </summary>
public class FederatedClientWithoutProviderTests(WithoutProviderCountingFixture fixture) : IClassFixture<WithoutProviderCountingFixture>
{
    [Fact]
    public async Task TheHostWithoutProvider_MakesNoNetworkRequest()
    {
        // O boot já aconteceu no fixture, com o contador registrado antes dele; mais um pedido que não começa fluxo
        // (nenhum começa, sem provedor) e uma volta com state forjado.
        var client = SignedInUser.Anonymous(fixture);
        var providers = await client.GetAsync("/auth/federated/providers");
        var callback = await client.GetAsync("/auth/federated/callback?code=codigo&state=forjado");

        Assert.Equal("""{"provedores":[]}""", await providers.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.True(fixture.Outbound.Count == 0, $"pedidos de saída sem provedor: {string.Join(", ", fixture.Outbound.Hosts)}");
    }

    [Fact]
    public void TheHostWithoutProvider_ExposesTheSameEndpoints()
    {
        var actual = HostEndpointList.RoutesAndServerEndpoints(fixture.Services);

        var missing = ExpectedFromTheBaseline.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(ExpectedFromTheBaseline, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"os endpoints do host sem provedor mudaram contra a baseline. A mais: {string.Join("; ", extra)}. A menos: {string.Join("; ", missing)}.");

        // O cliente OIDC sem registração tem só o retorno, que já era rota mapeada sem provedor.
        Assert.Equal(["/auth/federated/callback"], HostEndpointList.ClientPaths(fixture.Services));
        Assert.Contains("GET /auth/federated/callback", actual);
    }

    /// <summary>
    /// A lista do host sem provedor no origin/main (<c>0bfa1cf</c>, antes do cliente sem registração), impressa no
    /// worktree da baseline pela tarefa 0.8 por um caso temporário, apagado depois.
    /// </summary>
    private static readonly string[] ExpectedFromTheBaseline =
    [
        "* /health",
        "DELETE /account/federated/{chave}",
        "DELETE /applications/{id:guid}/agents/{agentId:guid}",
        "DELETE /knowledge-bases/{id:guid}",
        "DELETE /knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}",
        "DELETE /sync/knowledge-bases/{knowledgeBaseId:guid}/documents",
        "DELETE /users/{id:guid}/federated/{chave}",
        "GET /account/",
        "GET /account/sessions/",
        "GET /account/two-factor/",
        "GET /agents/",
        "GET /agents/{id:guid}",
        "GET /agents/{id:guid}/.well-known/agent-card.json",
        "GET /agents/{id:guid}/applications",
        "GET /agents/{id:guid}/applications/count",
        "GET /applications/",
        "GET /applications/{id:guid}",
        "GET /applications/{id:guid}/agents",
        "GET /auth/federated/callback",
        "GET /auth/federated/providers",
        "GET /connect/authorize",
        "GET /connect/logout",
        "GET /identity",
        "GET /identity/federated-providers",
        "GET /insights/agents/{id:guid}",
        "GET /insights/system",
        "GET /knowledge-bases/",
        "GET /knowledge-bases/indexing-summary",
        "GET /knowledge-bases/{id:guid}",
        "GET /knowledge-bases/{knowledgeBaseId:guid}/document-events",
        "GET /knowledge-bases/{knowledgeBaseId:guid}/documents/",
        "GET /knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}",
        "GET /knowledge-index/diagnostics",
        "GET /mcp-servers/",
        "GET /mcp-servers/{id:guid}",
        "GET /mcp-servers/{id:guid}/tools",
        "GET /providers",
        "GET /sync/knowledge-bases/",
        "GET /sync/knowledge-bases/{knowledgeBaseId:guid}/documents",
        "GET /users/",
        "GET /users/{id:guid}",
        "POST /account/federated/{chave}/connect",
        "POST /account/password",
        "POST /account/sessions/revoke-others",
        "POST /account/sessions/{id}/revoke",
        "POST /account/two-factor/recovery-codes",
        "POST /agents/",
        "POST /agents/{id:guid}/activate",
        "POST /agents/{id:guid}/deactivate",
        "POST /agents/{id}/a2a",
        "POST /applications/",
        "POST /applications/{id:guid}/activate",
        "POST /applications/{id:guid}/agents",
        "POST /applications/{id:guid}/deactivate",
        "POST /applications/{id:guid}/secrets",
        "POST /applications/{id:guid}/secrets/{secretId:guid}/revoke",
        "POST /auth/federated/refusal",
        "POST /auth/federated/{chave}/start",
        "POST /auth/invitation",
        "POST /auth/invitation/accept",
        "POST /auth/invitation/federated/{chave}/start",
        "POST /auth/password-reset",
        "POST /auth/password-reset/complete",
        "POST /auth/session",
        "POST /auth/session/enrollment/confirm",
        "POST /auth/session/enrollment/setup",
        "POST /auth/session/second-factor",
        "POST /knowledge-bases/",
        "POST /knowledge-bases/{id:guid}/activate",
        "POST /knowledge-bases/{id:guid}/deactivate",
        "POST /knowledge-bases/{knowledgeBaseId:guid}/documents/",
        "POST /knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}/reindex",
        "POST /mcp-servers/",
        "POST /mcp-servers/test",
        "POST /mcp-servers/{id:guid}/activate",
        "POST /mcp-servers/{id:guid}/deactivate",
        "POST /mcp-servers/{id:guid}/test",
        "POST /sync/knowledge-bases/{knowledgeBaseId:guid}/sync-results",
        "POST /users/invitations",
        "POST /users/{id:guid}/activate",
        "POST /users/{id:guid}/deactivate",
        "POST /users/{id:guid}/invitation/cancel",
        "POST /users/{id:guid}/invitation/renew",
        "POST /users/{id:guid}/password-reset",
        "POST /users/{id:guid}/sessions/revoke",
        "POST /users/{id:guid}/two-factor/reset",
        "PUT /agents/{id:guid}",
        "PUT /agents/{id:guid}/delegations",
        "PUT /agents/{id:guid}/knowledge-bases",
        "PUT /agents/{id:guid}/mcp-servers",
        "PUT /applications/{id:guid}",
        "PUT /applications/{id:guid}/agents/{agentId:guid}",
        "PUT /knowledge-bases/{id:guid}",
        "PUT /knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}",
        "PUT /mcp-servers/{id:guid}",
        "PUT /sync/knowledge-bases/{knowledgeBaseId:guid}/documents",
        "PUT /users/{id:guid}",
        "PUT /users/{id:guid}/role",
        "oidc-servidor AuthorizationEndpointUris /connect/authorize",
        "oidc-servidor ConfigurationEndpointUris /.well-known/openid-configuration",
        "oidc-servidor EndSessionEndpointUris /connect/logout",
        "oidc-servidor JsonWebKeySetEndpointUris /.well-known/jwks",
        "oidc-servidor TokenEndpointUris /connect/token",

        // Acrescentadas pela #296 (change modelos-curados-por-provedor, D6): as quatro rotas de curadoria de modelos
        // por provedor, mapeadas com ou sem provedor federado. A asserção é a mesma: a lista inteira, nos dois sentidos.
        "DELETE /providers/{provider}/models",
        "GET /providers/models",
        "GET /providers/{provider}/vendor-models",
        "POST /providers/{provider}/models",
    ];
}
