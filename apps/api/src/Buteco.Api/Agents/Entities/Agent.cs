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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Agent()
    {
    }

    public Agent(string name, string instructions, string provider, string model)
    {
        Id = Guid.NewGuid();
        Name = name;
        Instructions = instructions;
        IsActive = true;
        Provider = provider;
        Model = model;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateDetails(string name, string instructions, string provider, string model)
    {
        Name = name;
        Instructions = instructions;
        Provider = provider;
        Model = model;
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
