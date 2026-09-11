namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Espelho de <c>Buteco.Api.Knowledge.Indexing.KnowledgeIndexingJobMessage</c>,
/// por cópia — os dois apps não se referenciam, mesma disciplina dos dois
/// <c>AppDbContext</c>.
/// </summary>
public sealed record KnowledgeIndexingJobMessage(
    Guid KnowledgeDocumentId,
    int ContentRevision,
    int Attempt = 1);
