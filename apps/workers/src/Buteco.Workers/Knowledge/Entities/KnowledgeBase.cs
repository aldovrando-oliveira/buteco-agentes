namespace Buteco.Workers.Knowledge.Entities;

/// <summary>
/// Espelho de leitura de <c>Buteco.Api.KnowledgeBases.Entities.KnowledgeBase</c>.
/// <c>apps/workers</c> não escreve neste catálogo nesta etapa — a entidade
/// existe para manter os dois <c>AppDbContext</c> sincronizados por disciplina,
/// e para que a resolução de tools da etapa de execução leia daqui.
/// </summary>
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
}
