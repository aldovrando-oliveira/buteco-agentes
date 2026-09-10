using System.Net;
using System.Net.Http.Json;
using Buteco.Api.AgentDelegations.Requests;
using Buteco.Api.AgentKnowledgeBindings.Requests;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Knowledge;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentKnowledgeBindingEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_InitialSet_LinksBases()
    {
        var agent = await CreateAgentAsync("Agente KB-A");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-A");

        var response = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, updated.KnowledgeBases[0].Id);
        Assert.Equal(knowledgeBase.Name, updated.KnowledgeBases[0].Name);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_DifferentSet_ReplacesLinks()
    {
        var agent = await CreateAgentAsync("Agente KB-B");
        var first = await _client.CreateBaseAsync("Base KB-B 1");
        var second = await _client.CreateBaseAsync("Base KB-B 2");

        await PutKnowledgeBasesAsync(agent.Id, [first.Id]);
        var response = await PutKnowledgeBasesAsync(agent.Id, [second.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
        Assert.Equal(second.Id, updated.KnowledgeBases[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_EmptyList_RemovesAllLinks()
    {
        var agent = await CreateAgentAsync("Agente KB-C");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-C");

        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);
        var response = await PutKnowledgeBasesAsync(agent.Id, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(updated!.KnowledgeBases);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_SameSetTwice_IsIdempotent()
    {
        var agent = await CreateAgentAsync("Agente KB-D");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-D");

        var first = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);
        var second = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = await second.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_InvalidKnowledgeBaseId_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var agent = await CreateAgentAsync("Agente KB-E");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-E");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        var response = await PutKnowledgeBasesAsync(agent.Id, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Rejeição atômica: o conjunto anterior fica intacto. É o par
        // "com item" do cenário de erro — sem ele o teste provaria apenas que
        // a API respondeu 400, não que ela não destruiu nada.
        var current = await GetAgentAsync(agent.Id);
        Assert.Single(current.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, current.KnowledgeBases[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_MissingAgent_ReturnsNotFound()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-F");

        var response = await PutKnowledgeBasesAsync(Guid.NewGuid(), [knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_MissingField_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var agent = await CreateAgentAsync("Agente KB-G");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-G");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        // Campo ausente é 400 explícito, nunca lista vazia silenciosa — que
        // removeria todos os vínculos do agente sem ninguém ter pedido.
        var response = await _client.PutAsJsonAsync(
            $"/agents/{agent.Id}/knowledge-bases",
            new ReplaceAgentKnowledgeBasesRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var current = await GetAgentAsync(agent.Id);
        Assert.Single(current.KnowledgeBases);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_DuplicateIdInPayload_LinksOnce()
    {
        var agent = await CreateAgentAsync("Agente KB-H");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-H");

        var response = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id, knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
    }

    // --- Estado inativo dos dois lados -------------------------------------

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_InactiveKnowledgeBase_IsAccepted()
    {
        var agent = await CreateAgentAsync("Agente KB-I");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-I");
        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", content: null);

        var response = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
    }

    [Fact]
    public async Task ReplaceAgentKnowledgeBases_InactiveAgent_IsAccepted()
    {
        // Desativar um agente impede que ele execute, não que o operador
        // configure os vínculos dele — mesmo motivo pelo qual a etapa 1
        // permite criar e atualizar documento em base inativa.
        var agent = await CreateAgentAsync("Agente KB-J");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-J");
        await _client.PostAsync($"/agents/{agent.Id}/deactivate", content: null);

        var response = await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.False(updated!.IsActive);
        Assert.Single(updated.KnowledgeBases);
    }

    [Fact]
    public async Task DeactivatingKnowledgeBase_KeepsExistingBinding()
    {
        var agent = await CreateAgentAsync("Agente KB-K");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-K");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", content: null);

        var afterDeactivate = await GetAgentAsync(agent.Id);
        Assert.Single(afterDeactivate.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, afterDeactivate.KnowledgeBases[0].Id);

        // Reativar não exige reconfigurar: nenhuma chamada ao PUT de vínculo
        // entre a desativação e a reativação.
        await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/activate", content: null);

        var afterActivate = await GetAgentAsync(agent.Id);
        Assert.Single(afterActivate.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, afterActivate.KnowledgeBases[0].Id);
    }

    // --- Exposição nas demais respostas de agente ---------------------------

    [Fact]
    public async Task GetAgentByIdAndListAgents_ReflectKnowledgeBases()
    {
        var agent = await CreateAgentAsync("Agente KB-L");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-L");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        var fetched = await GetAgentAsync(agent.Id);
        Assert.Single(fetched.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, fetched.KnowledgeBases[0].Id);

        var listed = await GetListedAgentAsync(agent.Id);
        Assert.Single(listed.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, listed.KnowledgeBases[0].Id);
    }

    [Fact]
    public async Task AgentWithoutKnowledgeBase_ReturnsEmptyList()
    {
        var agent = await CreateAgentAsync("Agente KB-M");

        // Recém-criado já nasce com lista vazia, não nula.
        Assert.NotNull(agent.KnowledgeBases);
        Assert.Empty(agent.KnowledgeBases);

        var fetched = await GetAgentAsync(agent.Id);
        Assert.NotNull(fetched.KnowledgeBases);
        Assert.Empty(fetched.KnowledgeBases);

        var listed = await GetListedAgentAsync(agent.Id);
        Assert.NotNull(listed.KnowledgeBases);
        Assert.Empty(listed.KnowledgeBases);
    }

    [Fact]
    public async Task UpdateAndActivationCycle_PreserveKnowledgeBases()
    {
        var agent = await CreateAgentAsync("Agente KB-N");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-N");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        var updateResponse = await _client.PutAsJsonAsync(
            $"/agents/{agent.Id}",
            new UpdateAgentRequest("Agente KB-N renomeado", "Outras instruções.", Provider, Model));
        var updated = await updateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, updated.KnowledgeBases[0].Id);

        var deactivateResponse = await _client.PostAsync($"/agents/{agent.Id}/deactivate", content: null);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(deactivated!.KnowledgeBases);

        var activateResponse = await _client.PostAsync($"/agents/{agent.Id}/activate", content: null);
        var activated = await activateResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(activated!.KnowledgeBases);
    }

    [Fact]
    public async Task ListAgents_WithSeveralBoundAgents_KeepsEachAgentsOwnBases()
    {
        var agentOne = await CreateAgentAsync("Agente KB-O 1");
        var agentTwo = await CreateAgentAsync("Agente KB-O 2");
        var agentThree = await CreateAgentAsync("Agente KB-O 3");

        var baseOne = await _client.CreateBaseAsync("Base KB-O 1");
        var baseTwo = await _client.CreateBaseAsync("Base KB-O 2");

        await PutKnowledgeBasesAsync(agentOne.Id, [baseOne.Id]);
        await PutKnowledgeBasesAsync(agentTwo.Id, [baseOne.Id, baseTwo.Id]);
        // agentThree fica deliberadamente sem vínculo — é o "sem item" do par.

        var listedOne = await GetListedAgentAsync(agentOne.Id);
        Assert.Equal([baseOne.Id], listedOne.KnowledgeBases.Select(kb => kb.Id));

        // Ordem esperada é a da resposta (por nome), não a de criação: "Base
        // KB-O 1" antes de "Base KB-O 2". Reordenar o resultado antes de
        // comparar tornaria a asserção cega à ordenação e, comparada contra a
        // ordem de criação, flake — os ids são Guid aleatório.
        var listedTwo = await GetListedAgentAsync(agentTwo.Id);
        Assert.Equal([baseOne.Id, baseTwo.Id], listedTwo.KnowledgeBases.Select(kb => kb.Id));

        var listedThree = await GetListedAgentAsync(agentThree.Id);
        Assert.Empty(listedThree.KnowledgeBases);
    }

    // --- Ponto cego do risco R3: os dois PUT vizinhos ----------------------

    [Fact]
    public async Task ReplaceAgentMcpServers_KeepsKnowledgeBasesInResponse()
    {
        var agent = await CreateAgentAsync("Agente KB-P");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-P");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        var mcpServer = await CreateMcpServerAsync("Servidor KB-P");
        var response = await _client.PutAsJsonAsync(
            $"/agents/{agent.Id}/mcp-servers",
            new ReplaceAgentMcpServersRequest([new AgentMcpServerBindingRequest(mcpServer.Id, [])]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, updated.KnowledgeBases[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_KeepsKnowledgeBasesInResponse()
    {
        var agent = await CreateAgentAsync("Agente KB-Q");
        var target = await CreateAgentAsync("Agente KB-Q Target");
        var knowledgeBase = await _client.CreateBaseAsync("Base KB-Q");
        await PutKnowledgeBasesAsync(agent.Id, [knowledgeBase.Id]);

        var response = await _client.PutAsJsonAsync(
            $"/agents/{agent.Id}/delegations",
            new ReplaceAgentDelegationsRequest([target.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.KnowledgeBases);
        Assert.Equal(knowledgeBase.Id, updated.KnowledgeBases[0].Id);
    }

    // --- Ordenação ---------------------------------------------------------

    [Fact]
    public async Task KnowledgeBases_AreOrderedByName()
    {
        var agent = await CreateAgentAsync("Agente KB-R");
        // Vinculadas fora de ordem alfabética de propósito.
        var zulu = await _client.CreateBaseAsync("KB-R Zulu");
        var alfa = await _client.CreateBaseAsync("KB-R Alfa");
        var mike = await _client.CreateBaseAsync("KB-R Mike");

        await PutKnowledgeBasesAsync(agent.Id, [zulu.Id, mike.Id, alfa.Id]);

        var fetched = await GetAgentAsync(agent.Id);
        Assert.Equal(
            ["KB-R Alfa", "KB-R Mike", "KB-R Zulu"],
            fetched.KnowledgeBases.Select(kb => kb.Name));
    }

    // Guarda do desempate (design.md D13, risco R7). Separado do teste acima
    // de propósito: remover o `.ThenBy(kb => kb.Id)` precisa reprovar ESTE e
    // não aquele — um guarda que reprova os dois está afirmando a garantia no
    // componente errado (convenção 15, segunda metade).
    //
    // A asserção é sobre a ordem CRESCENTE DE ID, e não sobre "duas consultas
    // devolvem a mesma ordem": esta segunda forma é asserção sobre
    // não-determinação e passa com o defeito presente sempre que o plano do
    // Postgres calhar de ser estável — exatamente o perfil dos quatro guardas
    // que esta base já teve de consertar.
    [Fact]
    public async Task KnowledgeBasesWithEqualNames_AreTieBrokenByIdDeterministically()
    {
        const string SharedName = "KB-S Homônima";

        var agent = await CreateAgentAsync("Agente KB-S");
        var first = await _client.CreateBaseAsync(SharedName);
        var second = await _client.CreateBaseAsync(SharedName);
        var third = await _client.CreateBaseAsync(SharedName);

        var byId = new[] { first.Id, second.Id, third.Id }.Order().ToList();

        // Vinculadas em ordem de inserção deliberadamente oposta à ordem de
        // id, para que a ordem "natural" do banco não coincida por acidente
        // com a esperada.
        await PutKnowledgeBasesAsync(agent.Id, byId.AsEnumerable().Reverse().ToList());

        var fetched = await GetAgentAsync(agent.Id);
        Assert.Equal(byId, fetched.KnowledgeBases.Select(kb => kb.Id));

        var listed = await GetListedAgentAsync(agent.Id);
        Assert.Equal(byId, listed.KnowledgeBases.Select(kb => kb.Id));
    }

    // Guarda de R2 para knowledgeBases — ver o comentário completo em
    // AgentMcpBindingEndpointsTests.McpServers_OrderedByDatabaseCollation_...
    // Este conjunto já tinha o desempate por id desde a etapa 3, e ainda assim
    // divergia entre as duas rotas: o desempate não alcança a divergência de
    // comparador, que está no critério primário.
    [Fact]
    public async Task KnowledgeBases_OrderedByDatabaseCollation_MatchesAcrossBothSurfaces()
    {
        var agent = await CreateAgentAsync("Agente KB-T");
        var upper = await _client.CreateBaseAsync("Base Suporte Alfa");
        var lower = await _client.CreateBaseAsync("base-suporte-alfa");

        await PutKnowledgeBasesAsync(agent.Id, [upper.Id, lower.Id]);

        var fetched = await GetAgentAsync(agent.Id);
        var listed = await GetListedAgentAsync(agent.Id);

        Assert.Equal(
            ["base-suporte-alfa", "Base Suporte Alfa"],
            fetched.KnowledgeBases.Select(kb => kb.Name));
        Assert.Equal(
            fetched.KnowledgeBases.Select(kb => kb.Id),
            listed.KnowledgeBases.Select(kb => kb.Id));
    }

    // --- Helpers ------------------------------------------------------------

    private Task<HttpResponseMessage> PutKnowledgeBasesAsync(Guid agentId, IReadOnlyList<Guid> knowledgeBaseIds) =>
        _client.PutAsJsonAsync($"/agents/{agentId}/knowledge-bases", new ReplaceAgentKnowledgeBasesRequest(knowledgeBaseIds));

    private async Task<AgentResponse> GetAgentAsync(Guid agentId)
    {
        var response = await _client.GetAsync($"/agents/{agentId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<AgentResponse> GetListedAgentAsync(Guid agentId)
    {
        var response = await _client.GetAsync("/agents");
        response.EnsureSuccessStatusCode();
        var agents = await response.Content.ReadFromJsonAsync<List<AgentResponse>>();
        return agents!.Single(agent => agent.Id == agentId);
    }

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name)
    {
        var response = await _client.PostAsJsonAsync(
            "/mcp-servers",
            new CreateMcpServerRequest(name, "Servidor de teste.", "https://mcp.exemplo.test/sse", "None", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
