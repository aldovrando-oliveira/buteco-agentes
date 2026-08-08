using System.Text.Json;
using A2A;

namespace Buteco.Workers.AgentDelegations;

/// <summary>
/// Leitura/escrita de <c>AgentTask.Metadata["delegationDepth"]</c> — quantos
/// saltos de delegação a cadeia já deu desde a mensagem original (ver
/// design.md da change apps-workers-delegacao-execucao, Decision 6).
/// Gravado já na criação da task delegada, não só na conclusão (diferente de
/// <c>conversationSession</c>, escrito só no caminho de sucesso pelo
/// <c>AgentExecutionService</c>).
/// </summary>
public static class DelegationDepth
{
    public const string MetadataKey = "delegationDepth";

    /// <summary>Ausência da chave (task raiz, nunca delegada) equivale a profundidade 0.</summary>
    public static int Read(AgentTask? task)
    {
        if (task?.Metadata is not null && task.Metadata.TryGetValue(MetadataKey, out var value))
        {
            return value.GetInt32();
        }

        return 0;
    }

    public static JsonElement Encode(int depth) => JsonSerializer.SerializeToElement(depth);
}
