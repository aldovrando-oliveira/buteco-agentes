namespace Buteco.Api.KnowledgeDocuments.Commands.DeleteKnowledgeDocument;

/// <summary>
/// Três desfechos desde a #102: o booleano anterior não tinha onde dizer "a base é
/// sincronizada" (design.md da change catalogo-base-sincronizada, D7).
/// </summary>
public enum DeleteKnowledgeDocumentResult
{
    Deleted,
    NotFound,
    SyncedKnowledgeBase,

    /// <summary>Duas falhas de concorrência seguidas: nada excluído, pode repetir (D10).</summary>
    ConcurrentWrite,
}
