namespace Buteco.Workers.Knowledge.Entities;

/// <summary>
/// Espelho de leitura de
/// <c>Buteco.Api.KnowledgeDocuments.Entities.KnowledgeDocument</c>.
/// </summary>
public class KnowledgeDocument
{
    public Guid Id { get; private set; }

    public Guid KnowledgeBaseId { get; private set; }

    public string Title { get; private set; } = null!;

    public string SourceType { get; private set; } = null!;

    public string ExtractedText { get; private set; } = null!;

    /// <summary>
    /// Coluna gerada pelo Postgres — nunca escrita por nenhum dos dois apps.
    /// </summary>
    public int ContentLengthBytes { get; private set; }

    public KnowledgeIndexingStatus IndexingStatus { get; private set; }

    public DateTimeOffset? IndexedAt { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// Lida pelo consumidor de indexação da etapa seguinte, que grava
    /// condicionado a ela ainda ser a corrente.
    /// </summary>
    public int ContentRevision { get; private set; }

    /// <summary>
    /// Nulo significa "nunca indexado sob a regra de ContentHash" — linha criada
    /// antes desta capability existir. Escrito por <c>apps/api</c>.
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>Escrito pelo consumidor de indexação deste app.</summary>
    public int FragmentCount { get; private set; }

    /// <inheritdoc cref="FragmentCount"/>
    public int IndexingAttempts { get; private set; }

    /// <inheritdoc cref="FragmentCount"/>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeDocument()
    {
    }
}
