using System.Text.Json;
using Buteco.Api.A2A;
using Buteco.Api.Agents.Responses;

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
            null, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], [],
            new AgentA2AAddresses("https://api.exemplo.com/a2a", "https://api.exemplo.com/card"));

        var json = JsonSerializer.Serialize(response, options);

        using var document = JsonDocument.Parse(json);
        Assert.True(
            document.RootElement.TryGetProperty("a2a", out _),
            $"chave 'a2a' ausente no JSON: {json}");
    }
}
