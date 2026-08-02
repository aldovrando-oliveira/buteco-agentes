using Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.AgentMcpBindings.Endpoints;

public static class AgentMcpBindingEndpoints
{
    public static IEndpointRouteBuilder MapAgentMcpBindingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/agents/{id:guid}/mcp-servers", ReplaceAgentMcpServersAsync);

        return app;
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound, ValidationProblem>> ReplaceAgentMcpServersAsync(
        Guid id,
        ReplaceAgentMcpServersRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.McpServerIds is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mcpServerIds"] = ["O conjunto de servidores MCP vinculados é obrigatório (envie uma lista vazia para remover todos os vínculos)."],
            });
        }

        var command = new ReplaceAgentMcpServersCommand(id, request.McpServerIds);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.AgentFound)
        {
            return TypedResults.NotFound();
        }

        if (result.InvalidMcpServerIds.Count > 0)
        {
            var invalidIds = string.Join(", ", result.InvalidMcpServerIds);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mcpServerIds"] = [$"Os seguintes ids não correspondem a nenhum servidor MCP cadastrado: {invalidIds}."],
            });
        }

        return TypedResults.Ok(result.Agent!);
    }
}
