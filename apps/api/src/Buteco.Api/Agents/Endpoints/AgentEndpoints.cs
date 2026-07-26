using Buteco.Api.A2A;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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
        AppDbContext dbContext,
        AgentA2AServerRegistry registry,
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

        var agent = new Entities.Agent(request.Name!, request.Instructions!);

        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync(cancellationToken);

        // A rota A2A do agente (/agents/{id}/a2a) resolve o A2AServer sob demanda a
        // partir deste registry; pré-populamos aqui para a primeira chamada já
        // responder sem precisar de uma consulta extra ao banco (ver RoutingA2ARequestHandler).
        registry.Register(agent.Id);

        var response = AgentResponse.FromEntity(agent);
        return TypedResults.Created($"/agents/{agent.Id}", response);
    }

    private static async Task<Ok<IReadOnlyList<AgentResponse>>> ListAgentsAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var agents = await dbContext.Agents
            .AsNoTracking()
            .OrderBy(agent => agent.CreatedAt)
            .Select(agent => AgentResponse.FromEntity(agent))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<AgentResponse>>(agents);
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound>> GetAgentByIdAsync(
        Guid id,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var agent = await dbContext.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(agent => agent.Id == id, cancellationToken);

        return agent is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AgentResponse.FromEntity(agent));
    }
}
