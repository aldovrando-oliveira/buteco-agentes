using System.Text.Json;
using System.Text.Json.Nodes;
using Buteco.Workers.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;

namespace Buteco.Workers.Tests.Agents;

/// <summary>
/// Prova, sem precisar de Postgres real, o problema que motivou
/// <see cref="ConversationSessionCodec"/> (design.md da change
/// apps-workers-historico-conversa, Decisão 10): a sessão serializada usa
/// polimorfismo baseado em "$type" (Microsoft.Extensions.AI.AIContent), que
/// exige "$type" como a primeira propriedade do objeto — e o jsonb do
/// Postgres não preserva ordem de propriedades. Simula a reordenação que o
/// jsonb faz reconstruindo a árvore JSON com as propriedades de cada objeto
/// invertidas.
/// </summary>
public class ConversationSessionCodecTests
{
    [Fact]
    public async Task RawSerializedSession_AfterPropertyReordering_FailsToDeserialize()
    {
        // Reproduz o bug real: sem o codec, guardar o JsonElement da sessão
        // diretamente e depois lê-lo de volta com as propriedades
        // reordenadas (como o jsonb faz) quebra a desserialização.
        var serializedSession = await BuildSerializedSessionWithOneMessageAsync("Qual é a capital da França?");

        var reordered = ReorderObjectProperties(serializedSession);

        // A desserialização do AgentSession em si é preguiçosa — o
        // JsonElement só é materializado em List<ChatMessage> quando algo
        // pede as mensagens (aqui, via InMemoryChatHistoryProvider.GetMessages;
        // em produção, dentro de RunAsync). É aí que o erro real acontece.
        await Assert.ThrowsAsync<JsonException>(async () =>
        {
            var provider = new InMemoryChatHistoryProvider();
            var restoredSession = await BuildAgent().DeserializeSessionAsync(reordered);
            provider.GetMessages(restoredSession);
        });
    }

    [Fact]
    public async Task EncodedSession_SurvivesPropertyReordering_AndDeserializesCorrectly()
    {
        var serializedSession = await BuildSerializedSessionWithOneMessageAsync("Qual é a capital da França?");

        var encoded = ConversationSessionCodec.Encode(serializedSession);

        // Simula o jsonb reordenando tudo ao redor do valor codificado (ex.
        // outras chaves do Metadata, do AgentTask) — o valor em si é uma
        // string escalar, opaca para o jsonb, então nada dentro dela muda.
        var wrappedInOuterStructure = new JsonObject
        {
            ["outraPropriedade"] = "qualquer coisa",
            ["conversationSession"] = JsonNode.Parse(encoded.GetRawText()),
        };
        var reorderedOuter = ReorderObjectProperties(
            JsonSerializer.SerializeToElement(wrappedInOuterStructure));

        var recoveredEncoded = reorderedOuter.GetProperty("conversationSession");
        var decoded = ConversationSessionCodec.Decode(recoveredEncoded);

        var agent = BuildAgent();
        var restoredSession = await agent.DeserializeSessionAsync(decoded);

        var provider = new InMemoryChatHistoryProvider();
        var messages = provider.GetMessages(restoredSession);
        Assert.Contains(messages, m => m.Text == "Qual é a capital da França?");
    }

    private static ChatClientAgent BuildAgent() => new(new Mock<IChatClient>().Object);

    private static async Task<JsonElement> BuildSerializedSessionWithOneMessageAsync(string userText)
    {
        var provider = new InMemoryChatHistoryProvider();
        var agent = new ChatClientAgent(new Mock<IChatClient>().Object, new ChatClientAgentOptions
        {
            ChatHistoryProvider = provider,
        });

        var session = await agent.CreateSessionAsync();
        provider.SetMessages(session, [new ChatMessage(ChatRole.User, userText)]);

        return await agent.SerializeSessionAsync(session);
    }

    private static JsonElement ReorderObjectProperties(JsonElement element)
    {
        var node = JsonNode.Parse(element.GetRawText());
        var reordered = ReorderObjectProperties(node);
        return JsonSerializer.SerializeToElement(reordered);
    }

    private static JsonNode? ReorderObjectProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var reorderedObject = new JsonObject();
                foreach (var (key, value) in obj.Reverse())
                {
                    reorderedObject.Add(key, ReorderObjectProperties(value?.DeepClone()));
                }

                return reorderedObject;
            case JsonArray array:
                var reorderedArray = new JsonArray();
                foreach (var item in array)
                {
                    reorderedArray.Add(ReorderObjectProperties(item?.DeepClone()));
                }

                return reorderedArray;
            default:
                return node?.DeepClone();
        }
    }
}
