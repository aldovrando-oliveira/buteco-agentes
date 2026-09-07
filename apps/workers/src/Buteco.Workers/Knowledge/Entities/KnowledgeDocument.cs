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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeDocument()
    {
    }
}
