using Buteco.Api.Agents.Entities;
using Mediator;

namespace Buteco.Api.Agents.Commands.UpdateAgent;

public sealed record UpdateAgentCommand(
    Guid Id,
    string Name,
    string Instructions,
    string Provider,
    string Model,
    string? Description,
    IReadOnlyList<Skill> Skills) : ICommand<UpdateAgentResult>;
