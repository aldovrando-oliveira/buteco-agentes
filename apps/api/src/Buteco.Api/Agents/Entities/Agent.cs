namespace Buteco.Api.Agents.Entities;

public class Agent
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public bool IsActive { get; private set; }

    // Nullable: agentes cadastrados antes desta capacidade existir não têm
    // valor para nenhum dos dois — não existe default seguro e universal
    // (não dá para assumir "todos eram OpenAI"). Um agente com Provider ou
    // Model nulos está no estado implícito "precisa de reconfiguração" (ver
    // design.md da change backend-multi-provedor-llm, Decision 6). O
    // construtor e UpdateDetails exigem os dois não-nulos porque toda
    // criação/edição feita por código já validou a combinação contra o
    // catálogo disponível (Decision 4) — só a migration pode deixar um
    // agente nesse estado.
    public string? Provider { get; private set; }

    public string? Model { get; private set; }

    // Opcional por natureza — nem todo agente precisa de descrição textual
    // (diferente de Provider/Model, sem os quais o agente não processa
    // nenhuma task). Não é migration-artifact como Provider/Model: nenhum
    // agente existente tem Description até esta coluna existir, e o default
    // (nulo) é seguro e universal para todos (Decision 4 do design.md da
    // change backend-agente-description-skills).
    public string? Description { get; private set; }

    public IReadOnlyList<Skill> Skills { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Agent()
    {
    }

    public Agent(string name, string instructions, string provider, string model, string? description, IReadOnlyList<Skill> skills)
    {
        Id = Guid.NewGuid();
        Name = name;
        Instructions = instructions;
        IsActive = true;
        Provider = provider;
        Model = model;
        Description = description;
        Skills = skills;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateDetails(string name, string instructions, string provider, string model, string? description, IReadOnlyList<Skill> skills)
    {
        Name = name;
        Instructions = instructions;
        Provider = provider;
        Model = model;
        Description = description;
        Skills = skills;
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
