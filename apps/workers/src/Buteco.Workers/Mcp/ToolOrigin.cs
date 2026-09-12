namespace Buteco.Workers.Mcp;

/// <summary>
/// Conjunto de origem de uma tool no espaço de nome compartilhado que
/// <see cref="ToolNameDeduplicator"/> administra. Existe para o aviso de
/// colisão poder dizer ao operador de onde vem cada lado — sem isso, o sintoma
/// "cadastrei a tool e o agente chama outra" não tem rastro que o explique.
/// </summary>
/// <remarks>
/// A ordem dos valores acompanha a precedência declarada em
/// <see cref="ToolNameDeduplicator.Deduplicate"/> — MCP mantém o nome,
/// delegação é renomeada contra MCP, e conhecimento é renomeado contra os dois.
/// Não é a ordem que implementa a precedência (a ordem dos parâmetros é), mas
/// mantê-las coerentes evita que alguém leia uma e conclua a outra.
/// </remarks>
public enum ToolOrigin
{
    Mcp,
    Delegation,
    Knowledge,
}
