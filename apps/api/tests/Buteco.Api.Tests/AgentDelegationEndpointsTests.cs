using System.Net;
using System.Net.Http.Json;
using Buteco.Api.AgentDelegations.Requests;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class AgentDelegationEndpointsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReplaceAgentDelegations_InitialSet_LinksDelegations()
    {
        var source = await CreateAgentAsync("Agente A");
        var target = await CreateAgentAsync("Agente A Target");

        var response = await PutDelegationsAsync(source.Id, [target.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.DelegatesTo);
        Assert.Equal(target.Id, updated.DelegatesTo[0].Id);
        Assert.Equal(target.Name, updated.DelegatesTo[0].Name);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_DifferentSet_ReplacesLinks()
    {
        var source = await CreateAgentAsync("Agente B");
        var targetOne = await CreateAgentAsync("Agente B Target 1");
        var targetTwo = await CreateAgentAsync("Agente B Target 2");

        await PutDelegationsAsync(source.Id, [targetOne.Id]);
        var response = await PutDelegationsAsync(source.Id, [targetTwo.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.DelegatesTo);
        Assert.Equal(targetTwo.Id, updated.DelegatesTo[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_EmptyList_RemovesAllLinks()
    {
        var source = await CreateAgentAsync("Agente C");
        var target = await CreateAgentAsync("Agente C Target");

        await PutDelegationsAsync(source.Id, [target.Id]);
        var response = await PutDelegationsAsync(source.Id, []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Empty(updated!.DelegatesTo);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_SameSetTwice_IsIdempotent()
    {
        var source = await CreateAgentAsync("Agente D");
        var target = await CreateAgentAsync("Agente D Target");

        var first = await PutDelegationsAsync(source.Id, [target.Id]);
        var second = await PutDelegationsAsync(source.Id, [target.Id]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = await second.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.DelegatesTo);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_InvalidTargetAgentId_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var source = await CreateAgentAsync("Agente E");
        var target = await CreateAgentAsync("Agente E Target");
        await PutDelegationsAsync(source.Id, [target.Id]);

        var response = await PutDelegationsAsync(source.Id, [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{source.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(current!.DelegatesTo);
        Assert.Equal(target.Id, current.DelegatesTo[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_SelfDelegation_ReturnsValidationProblemAndKeepsExistingLinks()
    {
        var source = await CreateAgentAsync("Agente F");
        var target = await CreateAgentAsync("Agente F Target");
        await PutDelegationsAsync(source.Id, [target.Id]);

        var response = await PutDelegationsAsync(source.Id, [source.Id]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var getResponse = await _client.GetAsync($"/agents/{source.Id}");
        var current = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(current!.DelegatesTo);
        Assert.Equal(target.Id, current.DelegatesTo[0].Id);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_MissingSourceAgent_ReturnsNotFound()
    {
        var target = await CreateAgentAsync("Agente G Target");

        var response = await PutDelegationsAsync(Guid.NewGuid(), [target.Id]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_InactiveTargetAgent_IsAccepted()
    {
        var source = await CreateAgentAsync("Agente H");
        var target = await CreateAgentAsync("Agente H Target");
        await _client.PostAsync($"/agents/{target.Id}/deactivate", content: null);

        var response = await PutDelegationsAsync(source.Id, [target.Id]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(updated!.DelegatesTo);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_IndirectCycle_IsPermitted()
    {
        var agentA = await CreateAgentAsync("Agente I-A");
        var agentB = await CreateAgentAsync("Agente I-B");
        var agentC = await CreateAgentAsync("Agente I-C");

        var responseAtoB = await PutDelegationsAsync(agentA.Id, [agentB.Id]);
        var responseBtoC = await PutDelegationsAsync(agentB.Id, [agentC.Id]);
        var responseCtoA = await PutDelegationsAsync(agentC.Id, [agentA.Id]);

        Assert.Equal(HttpStatusCode.OK, responseAtoB.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseBtoC.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseCtoA.StatusCode);
    }

    [Fact]
    public async Task ReplaceAgentDelegations_BidirectionalPair_IsPermitted()
    {
        var agentA = await CreateAgentAsync("Agente J-A");
        var agentB = await CreateAgentAsync("Agente J-B");

        var responseAtoB = await PutDelegationsAsync(agentA.Id, [agentB.Id]);
        var responseBtoA = await PutDelegationsAsync(agentB.Id, [agentA.Id]);

        Assert.Equal(HttpStatusCode.OK, responseAtoB.StatusCode);
        Assert.Equal(HttpStatusCode.OK, responseBtoA.StatusCode);

        var getAResponse = await _client.GetAsync($"/agents/{agentA.Id}");
        var agentAUpdated = await getAResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(agentAUpdated!.DelegatesTo);
        Assert.Equal(agentB.Id, agentAUpdated.DelegatesTo[0].Id);
    }

    [Fact]
    public async Task GetAgentByIdAndListAgents_ReflectDelegatesTo()
    {
        var source = await CreateAgentAsync("Agente K");
        var target = await CreateAgentAsync("Agente K Target");
        await PutDelegationsAsync(source.Id, [target.Id]);

        var getResponse = await _client.GetAsync($"/agents/{source.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.Single(fetched!.DelegatesTo);
        Assert.Equal(target.Id, fetched.DelegatesTo[0].Id);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == source.Id);
        Assert.Single(listed.DelegatesTo);
        Assert.Equal(target.Id, listed.DelegatesTo[0].Id);
    }

    [Fact]
    public async Task AgentWithoutDelegation_ReturnsEmptyDelegatesToList()
    {
        var agent = await CreateAgentAsync("Agente L");

        var getResponse = await _client.GetAsync($"/agents/{agent.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<AgentResponse>();
        Assert.NotNull(fetched!.DelegatesTo);
        Assert.Empty(fetched.DelegatesTo);

        var listResponse = await _client.GetAsync("/agents");
        var agents = await listResponse.Content.ReadFromJsonAsync<List<AgentResponse>>();
        var listed = agents!.Single(a => a.Id == agent.Id);
        Assert.NotNull(listed.DelegatesTo);
        Assert.Empty(listed.DelegatesTo);
    }

    // --- Ordenação ---------------------------------------------------------

    [Fact]
    public async Task DelegatesTo_AreOrderedByName()
    {
        var source = await CreateAgentAsync("Agente M");
        // Vinculados fora de ordem alfabética de propósito.
        var zulu = await CreateAgentAsync("Agente M Zulu");
        var alfa = await CreateAgentAsync("Agente M Alfa");
        var mike = await CreateAgentAsync("Agente M Mike");

        await PutDelegationsAsync(source.Id, [zulu.Id, mike.Id, alfa.Id]);

        var fetched = await GetAgentAsync(source.Id);
        Assert.Equal(
            ["Agente M Alfa", "Agente M Mike", "Agente M Zulu"],
            fetched.DelegatesTo.Select(target => target.Name));

        // Par "sem empate" (convenção 5): nomes todos distintos, a ordem é a do
        // critério primário e o desempate não a altera.
        var listed = await GetListedAgentAsync(source.Id);
        Assert.Equal(
            ["Agente M Alfa", "Agente M Mike", "Agente M Zulu"],
            listed.DelegatesTo.Select(target => target.Name));
    }

    // Guarda do desempate (api-response-ordering). Separado do teste acima de
    // propósito: remover o `.ThenBy(agent => agent.Id)` precisa reprovar ESTE e
    // não aquele — um guarda que reprova os dois está afirmando a garantia no
    // componente errado (convenção 15, segunda metade).
    //
    // A asserção é sobre a ordem CRESCENTE DE ID, e não sobre "duas consultas
    // devolvem a mesma ordem": esta segunda forma é asserção sobre
    // não-determinação e passa com o defeito presente sempre que o plano do
    // Postgres calhar de ser estável.
    [Fact]
    public async Task DelegatesToWithEqualNames_AreTieBrokenByIdDeterministically()
    {
        const string SharedName = "Agente N Homônimo";

        var source = await CreateAgentAsync("Agente N");
        var first = await CreateAgentAsync(SharedName);
        var second = await CreateAgentAsync(SharedName);
        var third = await CreateAgentAsync(SharedName);

        var byId = new[] { first.Id, second.Id, third.Id }.Order().ToList();

        // Vinculados em ordem de inserção deliberadamente oposta à ordem de id,
        // para que a ordem "natural" do banco não coincida por acidente com a
        // esperada.
        await PutDelegationsAsync(source.Id, byId.AsEnumerable().Reverse().ToList());

        var fetched = await GetAgentAsync(source.Id);
        Assert.Equal(byId, fetched.DelegatesTo.Select(target => target.Id));

        var listed = await GetListedAgentAsync(source.Id);
        Assert.Equal(byId, listed.DelegatesTo.Select(target => target.Id));
    }

    // Metade determinística do guarda de desempate (design.md, D6 "Correção
    // feita durante a implementação"). O guarda comportamental acima reprova só
    // probabilisticamente: sem o `ThenBy`, o Postgres às vezes já devolve em
    // ordem de id sozinho — um index scan pela chave primária emite exatamente
    // assim, e "ordenado por id porque o ThenBy existe" é indistinguível de
    // "ordenado por id porque o plano calhou".
    //
    // Esta asserção é sobre o SQL que a consulta DE PRODUÇÃO emitiu na
    // requisição real, capturado do log do EF Core — não uma consulta remontada
    // no teste, que passaria igual com o comportamento certo e com o errado
    // (convenção 11). Reprova no instante em que o `ThenBy` sai, sempre.
    [Fact]
    public async Task DelegatesToQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        var source = await CreateAgentAsync("Agente O");
        var target = await CreateAgentAsync("Agente O Target");
        await PutDelegationsAsync(source.Id, [target.Id]);

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            (await _client.GetAsync($"/agents/{source.Id}")).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM agent_delegations", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    // Guarda de R2 para delegatesTo — ver o comentário completo em
    // AgentMcpBindingEndpointsTests.McpServers_OrderedByDatabaseCollation_...
    [Fact]
    public async Task DelegatesTo_OrderedByDatabaseCollation_MatchesAcrossBothSurfaces()
    {
        var source = await CreateAgentAsync("Agente P-Collation");
        var upper = await CreateAgentAsync("Agente Suporte Alfa");
        var lower = await CreateAgentAsync("agente-suporte-alfa");

        await PutDelegationsAsync(source.Id, [upper.Id, lower.Id]);

        var fetched = await GetAgentAsync(source.Id);
        var listed = await GetListedAgentAsync(source.Id);

        Assert.Equal(
            ["agente-suporte-alfa", "Agente Suporte Alfa"],
            fetched.DelegatesTo.Select(target => target.Name));
        Assert.Equal(
            fetched.DelegatesTo.Select(target => target.Id),
            listed.DelegatesTo.Select(target => target.Id));
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

    private Task<HttpResponseMessage> PutDelegationsAsync(Guid agentId, IReadOnlyList<Guid> targetAgentIds) =>
        _client.PutAsJsonAsync($"/agents/{agentId}/delegations", new ReplaceAgentDelegationsRequest(targetAgentIds));

    private async Task<AgentResponse> CreateAgentAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest(name, "Instruções.", Provider, Model));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }
}
