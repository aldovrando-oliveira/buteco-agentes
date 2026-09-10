using System.Net;
using System.Net.Http.Json;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentMcpBindingEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReplaceAgentMcpServers_InitialSet_LinksServers()
    {
        var agent = await CreateAgentAsync("Agente A");
        var mcpServer = await CreateMcpServerAsync("MCP A");

        var response = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
        Assert.Equal(mcpServer.Id, updated.McpServers[0].Id);
        Assert.Equal(mcpServer.Name, updated.McpServers[0].Name);
        // allowedTools: [] é um vínculo válido (Decision 3 do design.md da
        // change backend-mcp-selecao-tools) — estes testes não exercitam
        // seleção de tools, só o gerenciamento do vínculo em si.
        Assert.Empty(updated.McpServers[0].AllowedTools);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_DifferentSet_ReplacesLinks()
    {
        var agent = await CreateAgentAsync("Agente B");
        var mcpServerOne = await CreateMcpServerAsync("MCP B1");
        var mcpServerTwo = await CreateMcpServerAsync("MCP B2");

        await PutMcpServersAsync(agent.Id, [mcpServerOne.Id]);
        var response = await PutMcpServersAsync(agent.Id, [mcpServerTwo.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
        Assert.Equal(mcpServerTwo.Id, updated.McpServers[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_EmptyList_RemovesAllLinks()
    {
        var agent = await CreateAgentAsync("Agente C");
        var mcpServer = await CreateMcpServerAsync("MCP C");

        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);
        var response = await PutMcpServersAsync(agent.Id, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(updated!.McpServers);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_SameSetTwice_IsIdempotent()
    {
        var agent = await CreateAgentAsync("Agente D");
        var mcpServer = await CreateMcpServerAsync("MCP D");

        var first = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);
        var second = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = await second.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_InvalidMcpServerId_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var agent = await CreateAgentAsync("Agente E");
        var mcpServer = await CreateMcpServerAsync("MCP E");
        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        var response = await PutMcpServersAsync(agent.Id, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(current!.McpServers);
        Assert.Equal(mcpServer.Id, current.McpServers[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_MissingAgent_ReturnsNotFound()
    {
        var response = await PutMcpServersAsync(Guid.NewGuid(), []);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAgentMcpServers_InactiveMcpServer_IsAccepted()
    {
        var agent = await CreateAgentAsync("Agente F");
        var mcpServer = await CreateMcpServerAsync("MCP F");
        await _client.PostAsync($"/mcp-servers/{mcpServer.Id}/deactivate", content: null);

        var response = await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.McpServers);
    }

    [Fact]
    public async Task SameMcpServer_LinkedToTwoAgents_NoInterference()
    {
        var agentOne = await CreateAgentAsync("Agente G1");
        var agentTwo = await CreateAgentAsync("Agente G2");
        var mcpServer = await CreateMcpServerAsync("MCP G Compartilhado");

        await PutMcpServersAsync(agentOne.Id, [mcpServer.Id]);
        await PutMcpServersAsync(agentTwo.Id, [mcpServer.Id]);

        var agentOneResponse = await _client.GetAsync($"/agents/{agentOne.Id}");
        var agentOneUpdated = await agentOneResponse.Content.ReadFromJsonAsync<AgentResponse>();
        var agentTwoResponse = await _client.GetAsync($"/agents/{agentTwo.Id}");
        var agentTwoUpdated = await agentTwoResponse.Content.ReadFromJsonAsync<AgentResponse>();

        Assert.Single(agentOneUpdated!.McpServers);
        Assert.Single(agentTwoUpdated!.McpServers);
        Assert.Equal(mcpServer.Id, agentOneUpdated.McpServers[0].Id);
        Assert.Equal(mcpServer.Id, agentTwoUpdated.McpServers[0].Id);
    }

    [Fact]
    public async Task SameAgent_LinkedToTwoMcpServers()
    {
        var agent = await CreateAgentAsync("Agente H");
        var mcpServerOne = await CreateMcpServerAsync("MCP H1");
        var mcpServerTwo = await CreateMcpServerAsync("MCP H2");

        var response = await PutMcpServersAsync(agent.Id, [mcpServerOne.Id, mcpServerTwo.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Equal(2, updated!.McpServers.Count);
        Assert.Contains(updated.McpServers, m => m.Id == mcpServerOne.Id);
        Assert.Contains(updated.McpServers, m => m.Id == mcpServerTwo.Id);
    }

    [Fact]
    public async Task GetAgentByIdAndListAgents_ReflectLinkedMcpServers()
    {
        var agent = await CreateAgentAsync("Agente I");
        var mcpServer = await CreateMcpServerAsync("MCP I");
        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(fetched!.McpServers);
        Assert.Equal(mcpServer.Id, fetched.McpServers[0].Id);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == agent.Id);
        Assert.Single(listed.McpServers);
        Assert.Equal(mcpServer.Id, listed.McpServers[0].Id);
    }

    [Fact]
    public async Task AgentWithoutBinding_ReturnsEmptyMcpServersList()
    {
        var agent = await CreateAgentAsync("Agente J");

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(fetched!.McpServers);
        Assert.Empty(fetched.McpServers);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == agent.Id);
        Assert.NotNull(listed.McpServers);
        Assert.Empty(listed.McpServers);
    }

    // --- Ordenação ---------------------------------------------------------

    [Fact]
    public async Task McpServers_AreOrderedByName()
    {
        var agent = await CreateAgentAsync("Agente K");
        // Vinculados fora de ordem alfabética de propósito.
        var zulu = await CreateMcpServerAsync("MCP K Zulu");
        var alfa = await CreateMcpServerAsync("MCP K Alfa");
        var mike = await CreateMcpServerAsync("MCP K Mike");

        await PutMcpServersAsync(agent.Id, [zulu.Id, mike.Id, alfa.Id]);

        var fetched = await GetAgentAsync(agent.Id);
        Assert.Equal(
            ["MCP K Alfa", "MCP K Mike", "MCP K Zulu"],
            fetched.McpServers.Select(mcpServer => mcpServer.Name));

        // Par "sem empate" (convenção 5): nomes todos distintos, a ordem é a do
        // critério primário e o desempate não a altera.
        var listed = await GetListedAgentAsync(agent.Id);
        Assert.Equal(
            ["MCP K Alfa", "MCP K Mike", "MCP K Zulu"],
            listed.McpServers.Select(mcpServer => mcpServer.Name));
    }

    // Guarda do desempate (api-response-ordering). Separado do teste acima de
    // propósito: remover o `.ThenBy(joined => joined.McpServer.Id)` precisa
    // reprovar ESTE e não aquele — um guarda que reprova os dois está afirmando
    // a garantia no componente errado (convenção 15, segunda metade).
    //
    // A asserção é sobre a ordem CRESCENTE DE ID, e não sobre "duas consultas
    // devolvem a mesma ordem": esta segunda forma é asserção sobre
    // não-determinação e passa com o defeito presente sempre que o plano do
    // Postgres calhar de ser estável.
    [Fact]
    public async Task McpServersWithEqualNames_AreTieBrokenByIdDeterministically()
    {
        const string SharedName = "MCP L Homônimo";

        var agent = await CreateAgentAsync("Agente L-Ordem");
        var first = await CreateMcpServerAsync(SharedName);
        var second = await CreateMcpServerAsync(SharedName);
        var third = await CreateMcpServerAsync(SharedName);

        var byId = new[] { first.Id, second.Id, third.Id }.Order().ToList();

        // Vinculados em ordem de inserção deliberadamente oposta à ordem de id,
        // para que a ordem "natural" do banco não coincida por acidente com a
        // esperada.
        await PutMcpServersAsync(agent.Id, byId.AsEnumerable().Reverse().ToList());

        var fetched = await GetAgentAsync(agent.Id);
        Assert.Equal(byId, fetched.McpServers.Select(mcpServer => mcpServer.Id));

        var listed = await GetListedAgentAsync(agent.Id);
        Assert.Equal(byId, listed.McpServers.Select(mcpServer => mcpServer.Id));
    }

    // Metade determinística do guarda de desempate — ver o comentário gêmeo em
    // AgentDelegationEndpointsTests. Este site é o que mostrou o problema: o
    // guarda comportamental acima passou com o defeito presente em 1 de 3
    // execuções desta classe.
    [Fact]
    public async Task McpServersQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        var agent = await CreateAgentAsync("Agente M-Ordem");
        var mcpServer = await CreateMcpServerAsync("MCP M Único");
        await PutMcpServersAsync(agent.Id, [mcpServer.Id]);

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            (await _client.GetAsync($"/agents/{agent.Id}")).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM agent_mcp_servers", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    // Guarda de R2 — divergência de COMPARADOR, não de desempate.
    //
    // GET /agents/{id} ordena em SQL (collation do Postgres) e GET /agents
    // ordenava em memória (comparador de string do .NET/ICU). Os dois discordam,
    // medido nos dois runtimes reais (design.md, Verificação 3):
    //
    //   postgres:18, datcollate=en_US.utf8, datlocprovider=c -> "mcp-suporte-alfa" antes de "MCP Suporte Alfa"
    //   .NET 10 / ICU (Invariant, pt-BR e en-US, os três iguais) -> o inverso
    //
    // Ou seja, sem empate de nome nenhum, as duas rotas podiam devolver o mesmo
    // agente em ordens diferentes. O ThenBy(Id) não alcança isso, porque a
    // divergência está no critério PRIMÁRIO. A correção é ordenar na consulta
    // (design.md, D3), e este guarda afirma a ordem esperada CONCRETAMENTE — a
    // do banco —, não só "as duas iguais": se as collations um dia convergirem
    // ele continua correto, e se divergirem de outro jeito ele reprova e obriga
    // a reconferir (R3).
    [Fact]
    public async Task McpServers_OrderedByDatabaseCollation_MatchesAcrossBothSurfaces()
    {
        var agent = await CreateAgentAsync("Agente N-Collation");
        var upper = await CreateMcpServerAsync("MCP Suporte Alfa");
        var lower = await CreateMcpServerAsync("mcp-suporte-alfa");

        await PutMcpServersAsync(agent.Id, [upper.Id, lower.Id]);

        var fetched = await GetAgentAsync(agent.Id);
        var listed = await GetListedAgentAsync(agent.Id);

        Assert.Equal(
            ["mcp-suporte-alfa", "MCP Suporte Alfa"],
            fetched.McpServers.Select(mcpServer => mcpServer.Name));
        Assert.Equal(
            fetched.McpServers.Select(mcpServer => mcpServer.Id),
            listed.McpServers.Select(mcpServer => mcpServer.Id));
    }

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

    // allowedTools vazio em todos os vínculos — estes testes cobrem o
    // gerenciamento do vínculo em si (herdados da change
    // backend-mcp-catalogo-vinculo), não a seleção de tools. allowedTools
    // vazio não exige handshake de validação (ver Decision 2 do design.md),
    // então este fixture não precisa simular um servidor MCP real.
    private Task<HttpResponseMessage> PutMcpServersAsync(Guid agentId, IReadOnlyList<Guid> mcpServerIds) =>
        _client.PutAsJsonAsync(
            $"/agents/{agentId}/mcp-servers",
            new ReplaceAgentMcpServersRequest(mcpServerIds.Select(id => new AgentMcpServerBindingRequest(id, [])).ToList()));

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private async Task<McpServerResponse> CreateMcpServerAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/mcp-servers", new CreateMcpServerRequest(name, "Descrição", "https://mcp.example.com", "None", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<McpServerResponse>())!;
    }
}
