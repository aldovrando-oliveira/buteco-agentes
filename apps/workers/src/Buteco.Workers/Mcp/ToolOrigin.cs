namespace Buteco.Workers.Mcp;

/// <summary>
/// Conjunto de origem de uma tool no espaço de nome compartilhado que
/// <see cref="ToolNameDeduplicator"/> administra. Existe para o aviso de
/// colisão poder dizer ao operador de onde vem cada lado — sem isso, o sintoma
/// "cadastrei a tool e o agente chama outra" não tem rastro que o explique.
/// </summary>
public enum ToolOrigin
{
    Mcp,
    Delegation,
}
