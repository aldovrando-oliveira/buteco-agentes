using System.Net;
using System.Net.Http.Json;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Classe própria porque o cenário exige um banco **sem nenhuma base**: cada
/// classe de teste recebe sua própria instância de <see cref="ApiFactoryFixture"/>
/// (e portanto seu próprio container), então este é o único lugar onde a
/// asserção de catálogo vazio não fica à mercê da ordem de execução.
///
/// É o par "sem item" da convenção 5 para a listagem.
/// </summary>
public class KnowledgeEmptyCatalogTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListKnowledgeBases_WithNoBases_ReturnsEmptyListNotNotFound()
    {
        var response = await _client.GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<KnowledgeBaseResponse>>())!);
    }

    /// <summary>
    /// Mesmo par "sem item", agora para o resumo de indexação: sem base nenhuma
    /// a resposta é lista vazia, nunca 404. Vive aqui pelo mesmo motivo que o
    /// cenário acima — é o único lugar com um banco garantidamente vazio.
    /// </summary>
    [Fact]
    public async Task IndexingSummary_WithNoBases_ReturnsEmptyListNotNotFound()
    {
        var response = await _client.GetAsync("/knowledge-bases/indexing-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<KnowledgeBaseIndexingSummaryResponse>>())!);
    }
}
