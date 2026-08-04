using Buteco.Api.Agents.Entities;
using Mediator;

namespace Buteco.Api.Agents.Commands.CreateAgent;

public sealed record CreateAgentCommand(
    string Name,
    string Instructions,
    string Provider,
    string Model,
    string? Description,
    IReadOnlyList<Skill> Skills) : ICommand<CreateAgentResult>;
