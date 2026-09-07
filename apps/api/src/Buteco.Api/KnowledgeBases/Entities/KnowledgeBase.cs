namespace Buteco.Api.KnowledgeBases.Entities;

/// <summary>
/// Base de conhecimento: um agrupamento nomeado de documentos que, a partir da
/// etapa de vínculo, pode ser ligado a N agentes.
/// </summary>
/// <remarks>
/// <see cref="Description"/> NÃO é texto decorativo de UI: na etapa de execução
/// é o texto que vira a descrição da tool exposta ao modelo, e é por ele que o
/// modelo decide se esta base é relevante para a pergunta. Por isso é
/// obrigatória e não vazia (design.md, proposal).
///
/// Sem exclusão, por decisão registrada em design.md (D6): é entidade de
/// catálogo, e a partir da etapa de vínculo terá agentes apontando para ela —
/// exatamente o caso que formou o padrão <c>IsActive</c> de <c>McpServer</c>.
/// </remarks>
public class KnowledgeBase
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private KnowledgeBase()
    {
    }

    public KnowledgeBase(string name, string description)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateDetails(string name, string description)
    {
        Name = name;
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
