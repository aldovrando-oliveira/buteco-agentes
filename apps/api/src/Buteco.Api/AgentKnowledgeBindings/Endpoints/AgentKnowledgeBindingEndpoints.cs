using Buteco.Api.AgentKnowledgeBindings.Commands.ReplaceAgentKnowledgeBases;
using Buteco.Api.AgentKnowledgeBindings.Requests;
using Buteco.Api.Agents.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.AgentKnowledgeBindings.Endpoints;

public static class AgentKnowledgeBindingEndpoints
{
    public static IEndpointRouteBuilder MapAgentKnowledgeBindingEndpoints(this IEndpointRouteBuilder app)
    {
        // Rota nomeada pela entidade do outro lado, como /mcp-servers, e não
        // pela relação, como /delegations (design.md, D3): /delegations existe
        // porque /agents/{id}/agents seria ilegível — os dois lados são Agent.
        // Aqui os lados são entidades diferentes, então o obstáculo não existe.
        //
        // Não há rota inversa GET /knowledge-bases/{id}/agents (D8): a visão
        // "quem usa esta base" é derivada no cliente a partir de GET /agents,
        // como McpServerAgentsCard já faz para servidores MCP.
        app.MapPut("/agents/{id:guid}/knowledge-bases", ReplaceAgentKnowledgeBasesAsync);

        return app;
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound, ValidationProblem>> ReplaceAgentKnowledgeBasesAsync(
        Guid id,
        ReplaceAgentKnowledgeBasesRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.KnowledgeBaseIds is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["knowledgeBaseIds"] = ["O conjunto de bases de conhecimento vinculadas é obrigatório (envie uma lista vazia para remover todos os vínculos)."],
            });
        }

        var command = new ReplaceAgentKnowledgeBasesCommand(id, request.KnowledgeBaseIds);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.AgentFound)
        {
            return TypedResults.NotFound();
        }

        if (result.InvalidKnowledgeBaseIds.Count > 0)
        {
            var invalidIds = string.Join(", ", result.InvalidKnowledgeBaseIds);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["knowledgeBaseIds"] = [$"Os seguintes ids não correspondem a nenhuma base de conhecimento cadastrada: {invalidIds}."],
            });
        }

        return TypedResults.Ok(result.Agent!);
    }
}
