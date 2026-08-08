namespace Buteco.Workers.Agents.Entities;

/// <summary>
/// Projeção independente da entidade <c>Agent</c> de <c>apps/api</c>, contra o
/// mesmo schema Postgres — sem <c>ProjectReference</c> entre os dois apps.
/// </summary>
public class Agent
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public bool IsActive { get; private set; }

    // Nullable: agentes cadastrados antes desta capacidade existir (ou nunca
    // reconfigurados) não têm valor para nenhum dos dois. apps/api já rejeita
    // SendMessage nesse caso antes de publicar o job (EnqueueingAgentHandler),
    // então na prática o worker só processa jobs com os dois preenchidos —
    // ver design.md da change backend-multi-provedor-llm, Decisions 5 e 6.
    public string? Provider { get; private set; }

    public string? Model { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Agent()
    {
    }
}
