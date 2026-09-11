using System.Net;
using System.Net.Http.Json;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.Tests.Support;

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
}
