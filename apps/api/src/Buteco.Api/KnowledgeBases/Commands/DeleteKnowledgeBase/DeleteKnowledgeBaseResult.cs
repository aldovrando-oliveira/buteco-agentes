namespace Buteco.Api.KnowledgeBases.Commands.DeleteKnowledgeBase;

/// <summary>
/// Desfechos da exclusão de base (design.md da change exclusao-base-conhecimento, D11).
/// </summary>
public enum DeleteKnowledgeBaseResult
{
    Deleted,
    NotFound,

    /// <summary>Base ativa: nada apagado; a exclusão exige a base desativada antes (D2).</summary>
    Active,

    /// <summary>Dois impasses seguidos: nada apagado, pode repetir (D6).</summary>
    ConcurrentWrite,
}
