using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

public class KnowledgeBaseCatalogTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<KnowledgeBaseResponse> CreateBaseAsync(string name = "Políticas", string description = "Políticas de troca e devolução.")
    {
        var response = await _client.PostAsJsonAsync("/knowledge-bases", new CreateKnowledgeBaseRequest(name, description));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!;
    }

    // --- Ordenação (api-response-ordering) ---------------------------------

    // Guarda comportamental do desempate do catálogo. O critério primário
    // continua sendo CreatedAt — catálogo em ordem de cadastro é o padrão dos
    // quatro catálogos de apps/api (design.md, D1), e esta change não o troca.
    [Fact]
    public async Task KnowledgeBasesWithEqualCreatedAt_AreTieBrokenById()
    {
        var first = await CreateBaseAsync("Base Empate KB-1");
        var second = await CreateBaseAsync("Base Empate KB-2");
        var third = await CreateBaseAsync("Base Empate KB-3");

        var tied = new[] { first.Id, second.Id, third.Id };
        await CreatedAtTie.ForceAsync(factory.Services, "knowledge_bases", tied);

        var response = await _client.GetAsync("/knowledge-bases");
        response.EnsureSuccessStatusCode();
        var listed = (await response.Content.ReadFromJsonAsync<List<KnowledgeBaseResponse>>())!;

        var observed = listed.Where(kb => tied.Contains(kb.Id)).Select(kb => kb.Id).ToList();
        Assert.Equal(tied.Order().ToList(), observed);
    }

    // Metade determinística (design.md, D6): o guarda acima reprova só
    // probabilisticamente, porque sem o ThenBy o Postgres às vezes já devolve em
    // ordem de id sozinho. Esta asserção é sobre o SQL da consulta de produção.
    [Fact]
    public async Task KnowledgeBaseCatalogQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        await CreateBaseAsync("Base SQL KB");

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            (await _client.GetAsync("/knowledge-bases")).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM knowledge_bases", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    [Fact]
    public async Task CreateKnowledgeBase_WithNameAndDescription_ReturnsCreatedActiveBase()
    {
        var request = new CreateKnowledgeBaseRequest("Políticas comerciais", "Regras de troca, devolução e reembolso.");

        var response = await _client.PostAsJsonAsync("/knowledge-bases", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var knowledgeBase = await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>();
        Assert.NotNull(knowledgeBase);
        Assert.NotEqual(Guid.Empty, knowledgeBase.Id);
        Assert.Equal(request.Name, knowledgeBase.Name);
        Assert.Equal(request.Description, knowledgeBase.Description);
        Assert.True(knowledgeBase.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateKnowledgeBase_WithoutName_ReturnsValidationProblem(string? name)
    {
        var response = await _client.PostAsJsonAsync("/knowledge-bases", new CreateKnowledgeBaseRequest(name, "Descrição válida."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // A descrição é obrigatória por ser o texto que o modelo lê para decidir se
    // a base é relevante — não é campo decorativo (design.md, proposal).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateKnowledgeBase_WithoutDescription_ReturnsValidationProblem(string? description)
    {
        var response = await _client.PostAsJsonAsync("/knowledge-bases", new CreateKnowledgeBaseRequest("Base", description));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateKnowledgeBase_WithDuplicateName_CreatesBothBases()
    {
        var first = await CreateBaseAsync("Nome repetido");
        var second = await CreateBaseAsync("Nome repetido");

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task ListKnowledgeBases_IncludesInactiveBases()
    {
        var knowledgeBase = await CreateBaseAsync("Base a desativar");
        var deactivated = await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null);
        deactivated.EnsureSuccessStatusCode();

        var response = await _client.GetAsync("/knowledge-bases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bases = await response.Content.ReadFromJsonAsync<List<KnowledgeBaseResponse>>();
        var found = Assert.Single(bases!, item => item.Id == knowledgeBase.Id);
        Assert.False(found.IsActive);
    }

    [Fact]
    public async Task GetKnowledgeBaseById_WhenExists_ReturnsFullData()
    {
        var created = await CreateBaseAsync("Base consultável", "Descrição da base consultável.");

        var response = await _client.GetAsync($"/knowledge-bases/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var knowledgeBase = await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>();
        Assert.Equal(created.Id, knowledgeBase!.Id);
        Assert.Equal("Base consultável", knowledgeBase.Name);
        Assert.Equal("Descrição da base consultável.", knowledgeBase.Description);
    }

    [Fact]
    public async Task GetKnowledgeBaseById_WhenMissing_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/knowledge-bases/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Base inativa não é "não encontrada": responde 200 com isActive false.
    [Fact]
    public async Task GetKnowledgeBaseById_WhenInactive_ReturnsOkWithIsActiveFalse()
    {
        var created = await CreateBaseAsync("Base inativa consultável");
        await _client.PostAsync($"/knowledge-bases/{created.Id}/deactivate", null);

        var response = await _client.GetAsync($"/knowledge-bases/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var knowledgeBase = await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>();
        Assert.False(knowledgeBase!.IsActive);
    }

    [Fact]
    public async Task UpdateKnowledgeBase_WhenExists_ReturnsUpdatedValues()
    {
        var created = await CreateBaseAsync("Nome antigo", "Descrição antiga.");

        var response = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{created.Id}",
            new UpdateKnowledgeBaseRequest("Nome novo", "Descrição nova."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var knowledgeBase = await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>();
        Assert.Equal("Nome novo", knowledgeBase!.Name);
        Assert.Equal("Descrição nova.", knowledgeBase.Description);
    }

    [Fact]
    public async Task UpdateKnowledgeBase_WhenMissing_ReturnsNotFound()
    {
        var response = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{Guid.NewGuid()}",
            new UpdateKnowledgeBaseRequest("Nome", "Descrição."));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateKnowledgeBase_WithEmptyDescription_KeepsPreviousValues()
    {
        var created = await CreateBaseAsync("Base preservada", "Descrição preservada.");

        var response = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{created.Id}",
            new UpdateKnowledgeBaseRequest("Nome novo", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var reread = await _client.GetFromJsonAsync<KnowledgeBaseResponse>($"/knowledge-bases/{created.Id}");
        Assert.Equal("Base preservada", reread!.Name);
        Assert.Equal("Descrição preservada.", reread.Description);
    }

    [Fact]
    public async Task DeactivateAndActivateKnowledgeBase_AreIdempotent()
    {
        var created = await CreateBaseAsync("Base de idempotência");

        var first = await _client.PostAsync($"/knowledge-bases/{created.Id}/deactivate", null);
        var second = await _client.PostAsync($"/knowledge-bases/{created.Id}/deactivate", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await second.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!.IsActive);

        var activated = await _client.PostAsync($"/knowledge-bases/{created.Id}/activate", null);
        Assert.True((await activated.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!.IsActive);
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("deactivate")]
    public async Task ActivateOrDeactivate_WhenMissing_ReturnsNotFound(string action)
    {
        var response = await _client.PostAsync($"/knowledge-bases/{Guid.NewGuid()}/{action}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Base de conhecimento não tem exclusão (design.md, D6) — só documento tem.
    // Responde 405, não 404: a rota /knowledge-bases/{id} existe para GET e
    // PUT, e o que falta é o verbo. É a resposta mais informativa das duas, e
    // afirmá-la é o que impede alguém acrescentar um MapDelete aqui sem
    // reabrir D6.
    [Fact]
    public async Task DeleteKnowledgeBase_IsNotAllowedAndBaseSurvives()
    {
        var created = await CreateBaseAsync("Base sem delete");

        var response = await _client.DeleteAsync($"/knowledge-bases/{created.Id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var reread = await _client.GetAsync($"/knowledge-bases/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
    }

    // --- A resposta de base não carrega contagem agregada -------------------

    /// <summary>
    /// Asserção <b>negativa</b>, e é ela que impede a regressão
    /// bem-intencionada de "só acrescentar o campo" (design.md, D1).
    ///
    /// <para>
    /// <c>KnowledgeBaseResponse</c> é construído em <b>seis</b> lugares, quatro
    /// deles handlers de comando sem relação nenhuma com indexação. Acrescentar
    /// contagem ali ou encareceria as quatro operações de escrita, ou as faria
    /// devolver zero — falso em atualizar, ativar e desativar, e a convenção 13
    /// o proíbe. O agregado vive em <c>GET /knowledge-bases/indexing-summary</c>.
    /// </para>
    ///
    /// <para>
    /// Sem este guarda o acréscimo passaria calado: nenhum sítio de construção
    /// está em <c>tests/</c>, e desserializar um campo zerado não reprova nada.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ListKnowledgeBases_DoesNotExposeDocumentOrIndexingCounts()
    {
        var knowledgeBase = await CreateBaseAsync("Base sem contagem");
        await _client.CreateDocumentAsync(knowledgeBase.Id, "Documento que não deve ser contado aqui");

        var json = await _client.GetStringAsync("/knowledge-bases");

        using var parsed = JsonDocument.Parse(json);
        var row = parsed.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == knowledgeBase.Id);

        foreach (var forbidden in new[] { "documentCount", "indexedCount", "failedCount", "documents", "indexingSummary" })
        {
            Assert.False(
                row.TryGetProperty(forbidden, out _),
                $"A resposta de base passou a carregar '{forbidden}'. O agregado pertence a "
              + "GET /knowledge-bases/indexing-summary (design.md, D1).");
        }
    }

    [Fact]
    public async Task CreateKnowledgeBase_DoesNotAssertAnyCount()
    {
        var response = await _client.PostAsJsonAsync(
            "/knowledge-bases", new CreateKnowledgeBaseRequest("Base recém-criada", "Descrição da base."));
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        using var parsed = JsonDocument.Parse(json);
        foreach (var forbidden in new[] { "documentCount", "indexedCount", "failedCount" })
        {
            Assert.False(parsed.RootElement.TryGetProperty(forbidden, out _), $"A criação passou a afirmar '{forbidden}'.");
        }
    }
}
