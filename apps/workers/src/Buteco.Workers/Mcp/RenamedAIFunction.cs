using Microsoft.Extensions.AI;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Expõe uma <see cref="AIFunction"/> já construída sob outro nome, sem tocar
/// em mais nada — usada por <see cref="ToolNameDeduplicator"/> para resolver
/// colisão de nome no conjunto final de tools.
/// </summary>
/// <remarks>
/// <para>
/// Encapsula em vez de reconstruir, e a diferença é o que torna o dedupe global
/// possível: uma <see cref="AIFunction"/> produzida por
/// <c>AIFunctionFactory.Create</c> captura o delegate num closure (no caso das
/// tools de delegação, com o <c>targetAgentId</c> dentro), e
/// <see cref="AIFunction"/> não expõe esse delegate. Reconstruir a função a
/// partir dela seria impossível; embrulhá-la não tem esse problema.
/// </para>
/// <para>
/// A classe base <see cref="DelegatingAIFunction"/> encaminha ao inner
/// <c>InvokeCoreAsync</c>, <c>Description</c>, <c>JsonSchema</c>,
/// <c>ReturnJsonSchema</c>, <c>JsonSerializerOptions</c>,
/// <c>UnderlyingMethod</c>, <c>AdditionalProperties</c> e
/// <c>GetService</c> — verificado por decompilação (design.md da change
/// dedupe-global-nome-de-tool, V6), não suposto. Só <see cref="Name"/> é
/// sobrescrito aqui. <b>Não sobrescrever nenhum dos outros</b>: é assim que se
/// perde o schema, ou o <c>GetService&lt;ApprovalRequiredAIFunction&gt;</c> que
/// <c>FunctionInvokingChatClient</c> consulta no caminho de aprovação.
/// </para>
/// <para>
/// No lado MCP o dedupe não usa esta classe: <c>McpClientTool.WithName</c> já
/// faz o mesmo preservando o tipo concreto e o <c>ProtocolTool</c> (V1), e é o
/// mecanismo que o próprio SDK do MCP documenta para colisão entre fontes.
/// </para>
/// </remarks>
internal sealed class RenamedAIFunction(AIFunction innerFunction, string name) : DelegatingAIFunction(innerFunction)
{
    public override string Name { get; } = name;
}
