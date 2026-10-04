using System.Collections.Concurrent;

namespace Buteco.Connectors.Sync;

/// <summary>
/// As recusas determinísticas lembradas entre ciclos (design.md da change
/// ciclo-de-sincronizacao, D14). O marcador gravado no <c>apps/api</c> só muda quando o
/// upsert é aceito, então um arquivo recusado seria exportado e enviado de novo a cada 5
/// minutos, para sempre. Com o mesmo marcador, o ciclo repõe a recusa daqui em vez de
/// baixar.
/// </summary>
/// <remarks>
/// <para>
/// Só as quatro recusas de conteúdo da #120 entram: o mesmo conteúdo produz a mesma
/// recusa, e o marcador do provedor muda quando o conteúdo muda. Falha transitória
/// (<c>provider-unavailable</c>, <c>provider-error</c>, <c>rate-limited</c>,
/// <c>file-not-found</c>, contenção) não diz nada sobre o conteúdo, e é recusada aqui
/// mesmo que alguém tente guardá-la.
/// </para>
/// <para>
/// Memória do processo, singleton: reiniciar apaga, e cada arquivo recusado é tentado uma
/// vez e volta a ser lembrado (D14, aceito). Uma instância só (D10).
/// </para>
/// </remarks>
public sealed class RefusalMemory
{
    private static readonly HashSet<string> DeterministicCodes =
        ["too-large", "unsupported-source-type", "null-character", "empty-content"];

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, Entry>> _byBase = new();

    public int Count => _byBase.Values.Sum(entries => entries.Count);

    public int CountFor(Guid knowledgeBaseId) => _byBase.TryGetValue(knowledgeBaseId, out var entries) ? entries.Count : 0;

    public static bool IsDeterministic(string code) => DeterministicCodes.Contains(code);

    /// <summary>A recusa guardada para esta referência, se o marcador for o mesmo.</summary>
    public bool TryRecall(Guid knowledgeBaseId, string externalRef, string externalVersion, out string code, out string? detail)
    {
        if (_byBase.TryGetValue(knowledgeBaseId, out var entries) &&
            entries.TryGetValue(externalRef, out var entry) &&
            entry.ExternalVersion == externalVersion)
        {
            code = entry.Code;
            detail = entry.Detail;
            return true;
        }

        code = "";
        detail = null;
        return false;
    }

    /// <summary>Guarda a recusa, se ela for determinística; qualquer outro código é ignorado.</summary>
    public void Remember(Guid knowledgeBaseId, string externalRef, string externalVersion, string code, string? detail)
    {
        if (!IsDeterministic(code))
        {
            return;
        }

        _byBase.GetOrAdd(knowledgeBaseId, _ => new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal))[externalRef] =
            new Entry(externalVersion, code, detail);
    }

    public void Forget(Guid knowledgeBaseId, string externalRef)
    {
        if (_byBase.TryGetValue(knowledgeBaseId, out var entries))
        {
            entries.TryRemove(externalRef, out _);
        }
    }

    /// <summary>
    /// Depois de uma listagem COMPLETA da base: sai o que não está mais na pasta. Uma
    /// listagem que falhou não diz o que sumiu, e não pode chamar isto.
    /// </summary>
    public void KeepOnly(Guid knowledgeBaseId, IReadOnlySet<string> presentRefs)
    {
        if (!_byBase.TryGetValue(knowledgeBaseId, out var entries))
        {
            return;
        }

        foreach (var externalRef in entries.Keys)
        {
            if (!presentRefs.Contains(externalRef))
            {
                entries.TryRemove(externalRef, out _);
            }
        }
    }

    public void ForgetBase(Guid knowledgeBaseId) => _byBase.TryRemove(knowledgeBaseId, out _);

    /// <summary>No fim de uma rodada completa: saem as bases que não estão mais em <c>GET /sync/knowledge-bases</c>.</summary>
    public void KeepOnlyBases(IReadOnlySet<Guid> knowledgeBaseIds)
    {
        foreach (var knowledgeBaseId in _byBase.Keys)
        {
            if (!knowledgeBaseIds.Contains(knowledgeBaseId))
            {
                _byBase.TryRemove(knowledgeBaseId, out _);
            }
        }
    }

    private sealed record Entry(string ExternalVersion, string Code, string? Detail);
}
