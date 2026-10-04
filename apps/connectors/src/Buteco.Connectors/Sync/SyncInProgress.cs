using System.Collections.Concurrent;

namespace Buteco.Connectors.Sync;

/// <summary>
/// As bases com ciclo em curso nesta instância (design.md da change
/// ciclo-de-sincronizacao, D9): no máximo um ciclo por base, para a rodada periódica e
/// para o "Sincronizar agora". Em memória do processo, e por isso uma instância só (D10).
/// </summary>
public sealed class SyncInProgress
{
    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    /// <summary>Marca a base, se ninguém a marcou; quem recebe <c>true</c> chama <see cref="Finish"/>.</summary>
    public bool TryStart(Guid knowledgeBaseId) => _running.TryAdd(knowledgeBaseId, 0);

    public void Finish(Guid knowledgeBaseId) => _running.TryRemove(knowledgeBaseId, out _);
}
