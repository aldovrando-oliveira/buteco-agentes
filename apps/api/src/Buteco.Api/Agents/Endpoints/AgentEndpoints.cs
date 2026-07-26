using Buteco.Api.Agents.Commands.CreateAgent;
using Buteco.Api.Agents.Queries.GetAgentById;
using Buteco.Api.Agents.Queries.ListAgents;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.Agents.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/agents");

        group.MapPost("/", CreateAgentAsync);
        group.MapGet("/", ListAgentsAsync);
        group.MapGet("/{id:guid}", GetAgentByIdAsync);

        return app;
    }

    private static async Task<Results<Created<AgentResponse>, ValidationProblem>> CreateAgentAsync(
        CreateAgentRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["O nome do agente é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(request.Instructions))
        {
            errors["instructions"] = ["As instruções (system prompt) do agente são obrigatórias."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new CreateAgentCommand(request.Name!, request.Instructions!);
        var response = await mediator.Send(command, cancellationToken);

        return TypedResults.Created($"/agents/{response.Id}", response);
    }

    private static async Task<Ok<IReadOnlyList<AgentResponse>>> ListAgentsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var agents = await mediator.Send(new ListAgentsQuery(), cancellationToken);

        return TypedResults.Ok(agents);
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound>> GetAgentByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var agent = await mediator.Send(new GetAgentByIdQuery(id), cancellationToken);

        return agent is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(agent);
    }
}
