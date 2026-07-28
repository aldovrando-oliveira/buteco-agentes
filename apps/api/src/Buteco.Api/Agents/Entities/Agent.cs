namespace Buteco.Api.Agents.Entities;

public class Agent
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string Instructions { get; private set; } = null!;

    public bool IsActive { get; private set; }

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
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public void UpdateDetails(string name, string instructions)
    {
        Name = name;
        Instructions = instructions;
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
