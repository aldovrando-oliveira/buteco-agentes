namespace Buteco.Api.Agents.Entities;

public class Agent
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Agent()
    {
    }

    public Agent(string name, string instructions)
    {
        Id = Guid.NewGuid();
        Name = name;
        Instructions = instructions;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }
}
