using Buteco.Api.Agents.Commands.ActivateAgent;
using Buteco.Api.Agents.Commands.CreateAgent;
using Buteco.Api.Agents.Commands.DeactivateAgent;
using Buteco.Api.Agents.Commands.UpdateAgent;
using Buteco.Api.Agents.Entities;
using Buteco.Api.Agents.Queries.GetAgentById;
using Buteco.Api.Agents.Queries.ListAgents;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Providers;
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
        group.MapPut("/{id:guid}", UpdateAgentAsync);
        group.MapPost("/{id:guid}/activate", ActivateAgentAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateAgentAsync);

        return app;
    }

    private static async Task<Results<Created<AgentResponse>, ValidationProblem>> CreateAgentAsync(
        CreateAgentRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Name, request.Instructions, request.Provider, request.Model, request.Skills);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new CreateAgentCommand(request.Name!, request.Instructions!, request.Provider!, request.Model!, request.Description, ToSkills(request.Skills));
        var result = await mediator.Send(command, cancellationToken);

        return result.Validation switch
        {
            ProviderValidationOutcome.Valid => TypedResults.Created($"/agents/{result.Agent!.Id}", result.Agent),
            _ => TypedResults.ValidationProblem(BuildProviderValidationErrors(result.Validation)),
        };
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

    private static async Task<Results<Ok<AgentResponse>, NotFound, ValidationProblem>> UpdateAgentAsync(
        Guid id,
        UpdateAgentRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Name, request.Instructions, request.Provider, request.Model, request.Skills);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateAgentCommand(id, request.Name!, request.Instructions!, request.Provider!, request.Model!, request.Description, ToSkills(request.Skills));
        var result = await mediator.Send(command, cancellationToken);

        if (!result.Found)
        {
            return TypedResults.NotFound();
        }

        return result.Validation switch
        {
            ProviderValidationOutcome.Valid => TypedResults.Ok(result.Agent!),
            _ => TypedResults.ValidationProblem(BuildProviderValidationErrors(result.Validation)),
        };
    }

    private static Dictionary<string, string[]>? ValidateShape(
        string? name,
        string? instructions,
        string? provider,
        string? model,
        IReadOnlyList<SkillRequest>? skills)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome do agente é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(instructions))
        {
            errors["instructions"] = ["As instruções (system prompt) do agente são obrigatórias."];
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            errors["provider"] = ["O provedor de LLM do agente é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            errors["model"] = ["O modelo do agente é obrigatório."];
        }

        if (skills is not null)
        {
            for (var index = 0; index < skills.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(skills[index].Name))
                {
                    errors[$"skills[{index}].name"] = ["O nome da skill é obrigatório."];
                }
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    private static IReadOnlyList<Skill> ToSkills(IReadOnlyList<SkillRequest>? skills) =>
        skills?.Select(skill => new Skill(skill.Name!, skill.Description)).ToList() ?? [];

    private static Dictionary<string, string[]> BuildProviderValidationErrors(ProviderValidationOutcome validation) => validation switch
    {
        ProviderValidationOutcome.ProviderNotConfigured =>
            new Dictionary<string, string[]> { ["provider"] = ["O provedor informado não está configurado."] },
        ProviderValidationOutcome.ModelUnavailable =>
            new Dictionary<string, string[]> { ["model"] = ["O modelo informado não está disponível para o provedor."] },
        _ => throw new ArgumentOutOfRangeException(nameof(validation), validation, "Validação inesperada."),
    };

    private static async Task<Results<Ok<AgentResponse>, NotFound>> ActivateAgentAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new ActivateAgentCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound>> DeactivateAgentAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new DeactivateAgentCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }
}
