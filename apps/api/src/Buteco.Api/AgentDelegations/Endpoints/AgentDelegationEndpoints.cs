using Buteco.Api.AgentDelegations.Commands.ReplaceAgentDelegations;
using Buteco.Api.AgentDelegations.Requests;
using Buteco.Api.Agents.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.AgentDelegations.Endpoints;

public static class AgentDelegationEndpoints
{
    public static IEndpointRouteBuilder MapAgentDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/agents/{id:guid}/delegations", ReplaceAgentDelegationsAsync);

        return app;
    }

    private static async Task<Results<Ok<AgentResponse>, NotFound, ValidationProblem>> ReplaceAgentDelegationsAsync(
        Guid id,
        ReplaceAgentDelegationsRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (request.TargetAgentIds is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetAgentIds"] = ["O conjunto de agentes-alvo vinculados é obrigatório (envie uma lista vazia para remover todas as delegações)."],
            });
        }

        var command = new ReplaceAgentDelegationsCommand(id, request.TargetAgentIds);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.AgentFound)
        {
            return TypedResults.NotFound();
        }

        if (result.SelfDelegationRejected)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetAgentIds"] = ["Um agente não pode delegar para si mesmo."],
            });
        }

        if (result.CycleAgentNames.Count > 0)
        {
            // O caminho inteiro, e não só o fato: o vínculo a desfazer pode
            // estar em OUTRO agente — num ciclo A→B→C→A salvo a partir de A, é a
            // aresta C→A que o operador talvez queira remover. Sem o caminho, a
            // mensagem manda procurar (design.md, D5).
            var cyclePath = string.Join(" → ", result.CycleAgentNames);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetAgentIds"] = [$"Esta delegação fecha um ciclo entre agentes: {cyclePath}. Um agente não pode delegar, direta ou indiretamente, para um agente que delega de volta para ele."],
            });
        }

        if (result.InvalidTargetAgentIds.Count > 0)
        {
            var invalidIds = string.Join(", ", result.InvalidTargetAgentIds);
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["targetAgentIds"] = [$"Os seguintes ids não correspondem a nenhum agente cadastrado: {invalidIds}."],
            });
        }

        return TypedResults.Ok(result.Agent!);
    }
}
