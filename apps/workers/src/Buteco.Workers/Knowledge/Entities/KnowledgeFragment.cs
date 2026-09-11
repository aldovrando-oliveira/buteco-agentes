using Pgvector;

namespace Buteco.Workers.Knowledge.Entities;

/// <summary>
/// Espelho de <c>Buteco.Api.KnowledgeFragments.Entities.KnowledgeFragment</c>.
///
/// <para>
/// <b>Primeira entidade do domínio de conhecimento em que <c>apps/workers</c>
/// ESCREVE</b> — até aqui ele só espelhava schema para detectar divergência.
/// A tabela e a migração de banco real continuam nascendo em <c>apps/api</c>
/// (design.md, D10), que não escreve nela.
/// </para>
/// </summary>
public class KnowledgeFragment
{
    public Guid Id { get; private set; }

    public Guid KnowledgeDocumentId { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    public int Ordinal { get; private set; }

    public string Text { get; private set; } = null!;

    public Vector Embedding { get; private set; } = null!;

    /// <summary>
    /// Proveniência do vetor — o lado do índice na checagem bidirecional do boot
    /// (design.md, D6). Sem estas três colunas a checagem não tem contra o que
    /// comparar.
    /// </summary>
    public string EmbeddingProvider { get; private set; } = null!;

    /// <inheritdoc cref="EmbeddingProvider"/>
    public string EmbeddingModel { get; private set; } = null!;

    /// <inheritdoc cref="EmbeddingProvider"/>
    public int EmbeddingDimensions { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private KnowledgeFragment()
    {
    }

    public KnowledgeFragment(
        Guid knowledgeDocumentId,
        Guid knowledgeBaseId,
        int ordinal,
        string text,
        Vector embedding,
        string embeddingProvider,
        string embeddingModel,
        int embeddingDimensions)
    {
        Id = Guid.NewGuid();
        KnowledgeDocumentId = knowledgeDocumentId;
        KnowledgeBaseId = knowledgeBaseId;
        Ordinal = ordinal;
        Text = text;
        Embedding = embedding;
        EmbeddingProvider = embeddingProvider;
        EmbeddingModel = embeddingModel;
        EmbeddingDimensions = embeddingDimensions;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
