using System.Text.Json;
using global::A2A;

namespace Buteco.Workers.Agents;

/// <summary>
/// Codifica/decodifica o <c>AgentSession</c> serializado antes de guardá-lo
/// em <c>AgentTask.Metadata</c>.
/// </summary>
/// <remarks>
/// A sessão serializada usa polimorfismo baseado em <c>"$type"</c>
/// (<c>Microsoft.Extensions.AI.AIContent</c>), que o <c>System.Text.Json</c>
/// só consegue desserializar de volta se <c>"$type"</c> for a **primeira**
/// propriedade do objeto — e o <c>jsonb</c> do Postgres não preserva ordem de
/// propriedades (normaliza a estrutura ao gravar). Guardar o
/// <see cref="JsonElement"/> diretamente quebraria a leitura na próxima
/// execução assim que houvesse mais de uma sessão persistida. Por isso o
/// blob é reencodado como uma string JSON escapada — um valor escalar, opaco
/// para o <c>jsonb</c>, preservado byte a byte — em vez de estrutura
/// aninhada. Ver design.md da change apps-workers-historico-conversa,
/// Decisão 10.
///
/// A codificação em si precisa usar <see cref="A2AJsonUtilities.DefaultOptions"/>
/// — as mesmas opções que o resto do pipeline A2A usa para
/// serializar/desserializar o <c>AgentTask</c> inteiro (encoder de escaping,
/// entre outras). Usar as opções padrão do .NET aqui produz um encoding de
/// aspas diferente do que qualquer <c>PostgresTaskStore</c> grava de
/// verdade — não corrompe o dado (as duas formas decodificam igual), mas
/// quebra qualquer comparação textual bruta contra o valor persistido. Ver
/// design.md da change crossapp-session-codec-encoder, Decisão D1.
/// </remarks>
public static class ConversationSessionCodec
{
    public static JsonElement Encode(JsonElement serializedSession) =>
        JsonSerializer.SerializeToElement(serializedSession.GetRawText(), A2AJsonUtilities.DefaultOptions);

    public static JsonElement Decode(JsonElement encoded)
    {
        using var document = JsonDocument.Parse(encoded.GetString()!);
        return document.RootElement.Clone();
    }
}
