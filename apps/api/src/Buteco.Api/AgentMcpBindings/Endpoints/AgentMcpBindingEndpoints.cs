using Buteco.Api.AgentMcpBindings.Commands.ReplaceAgentMcpServers;
using Buteco.Api.AgentMcpBindings.Requests;
using Buteco.Api.Agents.Responses;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.AgentMcpBindings.Endpoints;

public static class AgentMcpBindingEndpoints
{
    public static IEndpointRouteBuilder MapAgentMcpBindingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/agents/{id:guid}/mcp-servers", ReplaceAgentMcpServersAsync);

        return app;
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound, ValidationProblem, ProblemHttpResult>> ReplaceAgentMcpServersAsync(
        Guid id,
        ReplaceAgentMcpServersRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.McpServers is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["mcpServers"] = ["O conjunto de servidores MCP vinculados é obrigatório (envie uma lista vazia para remover todos os vínculos)."],
            });
        }

        var bindings = request.McpServers
            .Select(binding => new ReplaceAgentMcpServersBinding(binding.McpServerId, binding.AllowedTools))
            .ToList();

        var command = new ReplaceAgentMcpServersCommand(id, bindings);
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
                ["mcpServers"] = [$"Os seguintes ids não correspondem a nenhum servidor MCP cadastrado: {invalidIds}."],
            });
        }

        if (result.InvalidTools.Count > 0)
        {
            var errors = result.InvalidTools.ToDictionary(
                invalidTool => $"mcpServers[{invalidTool.McpServerId}].allowedTools",
                invalidTool => (string[])[$"As seguintes tools não existem no servidor MCP {invalidTool.McpServerId}: {string.Join(", ", invalidTool.RejectedTools)}."]);

            return TypedResults.ValidationProblem(errors);
        }

        if (result.HandshakeFailure is not null)
        {
            // 502, não 400 (Decision 6 do design.md da change
            // backend-mcp-selecao-tools) — o payload em si é válido, a falha
            // é de um McpServer de terceiro inacessível durante a validação.
            return TypedResults.Problem(
                detail: result.HandshakeFailure.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: $"Não foi possível validar as tools do servidor MCP {result.HandshakeFailure.McpServerId}.");
        }

        return TypedResults.Ok(result.Agent!);
    }
}
