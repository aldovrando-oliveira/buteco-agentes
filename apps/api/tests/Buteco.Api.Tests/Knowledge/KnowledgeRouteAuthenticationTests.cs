using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Auth;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Nenhuma rota de conhecimento entra na allowlist de rotas anônimas: elas
/// caem na <c>FallbackPolicy</c> que exige usuário autenticado.
/// </summary>
public class KnowledgeRouteAuthenticationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private HttpClient UnauthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    [Fact]
    public async Task ListKnowledgeBases_WithoutToken_ReturnsUnauthorized()
    {
        var response = await UnauthenticatedClient().GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateKnowledgeBase_WithoutToken_ReturnsUnauthorizedAndCreatesNothing()
    {
        var client = UnauthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/knowledge-bases", new CreateKnowledgeBaseRequest("Base clandestina", "Descrição."));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var authenticated = factory.CreateClient();
        var body = await authenticated.GetStringAsync("/knowledge-bases");
        Assert.DoesNotContain("Base clandestina", body);
    }

    [Fact]
    public async Task ListKnowledgeDocuments_WithoutToken_ReturnsUnauthorized()
    {
        var knowledgeBase = await factory.CreateClient().CreateBaseAsync("Base autenticada");

        var response = await UnauthenticatedClient().GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateKnowledgeDocument_WithoutToken_ReturnsUnauthorized()
    {
        var knowledgeBase = await factory.CreateClient().CreateBaseAsync("Base para documento");

        var response = await UnauthenticatedClient().PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Título", "markdown", "# Conteúdo\n"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A exclusão é irreversível: sem token não pode nem chegar ao handler.
    [Fact]
    public async Task DeleteKnowledgeDocument_WithoutToken_ReturnsUnauthorizedAndDocumentSurvives()
    {
        var authenticated = factory.CreateClient();
        var knowledgeBase = await authenticated.CreateBaseAsync("Base protegida");
        var document = await authenticated.CreateDocumentAsync(knowledgeBase.Id, "Documento protegido");

        var response = await UnauthenticatedClient()
            .DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var reread = await authenticated.GetAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}");
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
    }

    [Fact]
    public async Task IndexingSummary_WithoutToken_ReturnsUnauthorized()
    {
        var response = await UnauthenticatedClient().GetAsync("/knowledge-bases/indexing-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A rota de diagnóstico do índice é global, fora do grupo de bases — e cai na
    // mesma FallbackPolicy. Ela NÃO entra na allowlist de rotas anônimas de
    // Program.cs: aquela lista é de rotas que precisam estar anônimas, e
    // ValidateRouteAuthenticationClassification reprovaria o boot se a rota
    // autenticada aparecesse lá. O guarda desta rota é este teste.
    [Fact]
    public async Task KnowledgeIndexDiagnostics_WithoutToken_ReturnsUnauthorized()
    {
        var response = await UnauthenticatedClient().GetAsync("/knowledge-index/diagnostics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReindexKnowledgeDocument_WithoutToken_ReturnsUnauthorizedAndDoesNotEnqueue()
    {
        var authenticated = factory.CreateClient();
        var knowledgeBase = await authenticated.CreateBaseAsync("Base reindex autenticada");
        var document = await authenticated.CreateDocumentAsync(knowledgeBase.Id, "Documento reindex autenticado");

        var before = factory.IndexingPublisher.PublishedFor(document.Id).Count;

        var response = await UnauthenticatedClient().PostAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}/reindex", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, factory.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    [Fact]
    public async Task ListDocumentEvents_WithoutToken_ReturnsUnauthorized()
    {
        var knowledgeBase = await factory.CreateClient().CreateBaseAsync("Base do histórico");

        var response = await UnauthenticatedClient().GetAsync(KnowledgeTestClient.DocumentEventsPath(knowledgeBase.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Hoje o 403 vem de graça — a rota não está na lista de
    /// <see cref="ServiceScopeAuthorizationHandler"/>. Está afirmado porque a #102
    /// vai mexer nesse escopo para acrescentar <c>service:connectors</c>, e é ali
    /// que uma liberação ampla demais escaparia sem teste (design.md da change
    /// historico-documentos-base, D8). Token emitido pelo mesmo
    /// <see cref="ITokenService"/> real, no molde de
    /// <c>ServiceScopeAuthorizationTests</c>.
    /// </summary>
    [Fact]
    public async Task ListDocumentEvents_WithServiceToken_ReturnsForbidden()
    {
        var knowledgeBase = await factory.CreateClient().CreateBaseAsync("Base do histórico");
        var client = factory.CreateClient();
        var tokenService = factory.Services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(ServiceScopeAuthorizationHandler.InboxSubject, TimeSpan.FromMinutes(5));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(KnowledgeTestClient.DocumentEventsPath(knowledgeBase.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
