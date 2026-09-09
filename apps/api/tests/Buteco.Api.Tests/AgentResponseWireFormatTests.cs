using System.Text.Json;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Responses;
using Buteco.Api.KnowledgeBases.Responses;

namespace Buteco.Api.Tests;

// O nome do campo no fio, e não o do record. Os demais testes desta suíte
// desserializam para AgentResponse, então passam pela mesma política de nomes
// na ida e na volta e são cegos a este erro: o painel lê a chave literal.
public class AgentResponseWireFormatTests
{
    [Fact]
    public void A2AAddresses_AreSerializedUnderTheKeyTheFrontendReads()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = new AgentResponse(
            Guid.NewGuid(), "Atendente", "Instruções.", true, "openai", "gpt-5.6-sol",
            null, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [], [],
            new AgentA2AAddresses("https://api.exemplo.com/a2a", "https://api.exemplo.com/card"));

        var json = JsonSerializer.Serialize(response, options);

        using var document = JsonDocument.Parse(json);
        Assert.True(
            document.RootElement.TryGetProperty("a2a", out _),
            $"chave 'a2a' ausente no JSON: {json}");
    }

    // Vínculo de conhecimento (change knowledge-base-vinculo-agente, D12).
    // Esta change NÃO introduz enum novo no fio, e `knowledgeBases` não tem
    // sigla nem maiúsculas consecutivas, então a armadilha específica de
    // `A2A` -> `a2A` não se aplica aqui. A asserção existe mesmo assim porque
    // qualquer erro de nome de chave é invisível para os testes que
    // desserializam para AgentResponse — eles passam pela mesma política nos
    // dois sentidos —, e o custo de não ser cego é esta asserção.
    [Fact]
    public void KnowledgeBases_AreSerializedUnderTheKeyTheFrontendReads()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var knowledgeBaseId = Guid.NewGuid();
        var response = new AgentResponse(
            Guid.NewGuid(), "Atendente", "Instruções.", true, "openai", "gpt-5.6-sol",
            null, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [],
            [new KnowledgeBaseSummaryResponse(knowledgeBaseId, "Política de trocas")],
            null);

        var json = JsonSerializer.Serialize(response, options);

        using var document = JsonDocument.Parse(json);
        Assert.True(
            document.RootElement.TryGetProperty("knowledgeBases", out var knowledgeBases),
            $"chave 'knowledgeBases' ausente no JSON: {json}");

        var item = Assert.Single(knowledgeBases.EnumerateArray().ToList());
        Assert.True(item.TryGetProperty("id", out var id), $"chave 'id' ausente no item: {json}");
        Assert.True(item.TryGetProperty("name", out var name), $"chave 'name' ausente no item: {json}");
        Assert.Equal(knowledgeBaseId, id.GetGuid());
        Assert.Equal("Política de trocas", name.GetString());
    }
}
