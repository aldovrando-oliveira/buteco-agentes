using System.Text.Json;
using A2A;

namespace Buteco.Workers.AgentDelegations;

/// <summary>
/// Leitura/escrita de <c>AgentTask.Metadata["delegationSourceAgentId"]</c> e
/// <c>["delegationSourceTaskId"]</c> — de qual agente, e de qual task dele, veio
/// uma task criada por delegação (design.md da change
/// <c>metricas-execucao-coleta</c>, D6). Gravadas junto de
/// <see cref="DelegationDepth"/>, no mesmo ponto, antes do primeiro
/// <c>SaveTaskAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que chave própria, e não inferir da profundidade.</b>
/// <see cref="DelegationDepth.Read"/> devolve 0 para chave ausente POR DESENHO,
/// então profundidade não separa "task raiz" de "task que nunca gravou a chave".
/// E derivar a origem da tabela de resultados de delegação não serve: aquela
/// linha é escrita pelo Source no fim da execução DELE — pode chegar depois da
/// linha do alvo, ou nunca, se o Source cair. A tela aprovada separa tempo de
/// fila por origem, e para isso a origem tem de estar na mesma linha.
/// </para>
///
/// <para>
/// <b>No <c>Metadata</c> da task, não no da mensagem:</b> é o que o worker já lê
/// para a profundidade, e <c>apps/api</c> nunca o preenche a partir do cliente —
/// uma chave de mensagem poderia vir de um cliente A2A externo.
/// </para>
/// </remarks>
public static class DelegationOrigin
{
    public const string SourceAgentIdKey = "delegationSourceAgentId";
    public const string SourceTaskIdKey = "delegationSourceTaskId";

    public static void Write(IDictionary<string, JsonElement> metadata, Guid sourceAgentId, string sourceTaskId)
    {
        metadata[SourceAgentIdKey] = JsonSerializer.SerializeToElement(sourceAgentId);
        metadata[SourceTaskIdKey] = JsonSerializer.SerializeToElement(sourceTaskId);
    }

    /// <summary>
    /// A origem gravada, ou nulo quando a task não nasceu de delegação. As duas
    /// chaves nascem juntas; se só uma estiver legível, a origem não é
    /// afirmada — nulo, em vez de meia origem.
    /// </summary>
    public static (Guid SourceAgentId, string SourceTaskId)? Read(AgentTask? task)
    {
        if (task?.Metadata is null
            || !task.Metadata.TryGetValue(SourceAgentIdKey, out var agentValue)
            || !task.Metadata.TryGetValue(SourceTaskIdKey, out var taskValue)
            || agentValue.ValueKind != JsonValueKind.String
            || taskValue.ValueKind != JsonValueKind.String
            || !agentValue.TryGetGuid(out var sourceAgentId))
        {
            return null;
        }

        var sourceTaskId = taskValue.GetString();
        return string.IsNullOrEmpty(sourceTaskId) ? null : (sourceAgentId, sourceTaskId);
    }
}
