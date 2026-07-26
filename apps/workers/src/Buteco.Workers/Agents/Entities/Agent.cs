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

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private Agent()
    {
    }
}
