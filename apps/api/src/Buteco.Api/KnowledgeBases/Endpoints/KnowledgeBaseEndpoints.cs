using Buteco.Api.KnowledgeBases.Commands.ActivateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.DeactivateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.UpdateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseById;
using Buteco.Api.KnowledgeBases.Queries.ListKnowledgeBases;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.KnowledgeBases.Endpoints;

public static class KnowledgeBaseEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeBaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/knowledge-bases");

        group.MapPost("/", CreateKnowledgeBaseAsync);
        group.MapGet("/", ListKnowledgeBasesAsync);
        group.MapGet("/{id:guid}", GetKnowledgeBaseByIdAsync);
        group.MapPut("/{id:guid}", UpdateKnowledgeBaseAsync);
        group.MapPost("/{id:guid}/activate", ActivateKnowledgeBaseAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateKnowledgeBaseAsync);

        // Sem MapDelete: base de conhecimento é entidade de catálogo e, a
        // partir da etapa de vínculo, terá agentes apontando para ela — mesmo
        // caso que formou o padrão IsActive de McpServer (design.md, D6).
        // Documento, que é conteúdo, tem exclusão real; ver
        // KnowledgeDocumentEndpoints.

        return app;
    }

    private static async Task<Results<Created<KnowledgeBaseResponse>, ValidationProblem>> CreateKnowledgeBaseAsync(
        CreateKnowledgeBaseRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = ValidateShape(request.Name, request.Description);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new CreateKnowledgeBaseCommand(request.Name!, request.Description!), cancellationToken);

        return TypedResults.Created($"/knowledge-bases/{result.Id}", result);
    }

    private static async Task<Ok<IReadOnlyList<KnowledgeBaseResponse>>> ListKnowledgeBasesAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var knowledgeBases = await mediator.Send(new ListKnowledgeBasesQuery(), cancellationToken);

        return TypedResults.Ok(knowledgeBases);
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound>> GetKnowledgeBaseByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var knowledgeBase = await mediator.Send(new GetKnowledgeBaseByIdQuery(id), cancellationToken);

        return knowledgeBase is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(knowledgeBase);
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound, ValidationProblem>> UpdateKnowledgeBaseAsync(
        Guid id,
        UpdateKnowledgeBaseRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = ValidateShape(request.Name, request.Description);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(new UpdateKnowledgeBaseCommand(id, request.Name!, request.Description!), cancellationToken);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound>> ActivateKnowledgeBaseAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new ActivateKnowledgeBaseCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound>> DeactivateKnowledgeBaseAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new DeactivateKnowledgeBaseCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    /// <summary>
    /// <c>Description</c> é obrigatória, diferente de <c>McpServer.Description</c>:
    /// na etapa de execução ela vira a descrição da tool exposta ao modelo, e é
    /// por ela que o modelo decide se a base é relevante. Base sem descrição
    /// seria uma tool que o modelo não sabe quando chamar.
    /// </summary>
    private static Dictionary<string, string[]>? ValidateShape(string? name, string? description)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome da base de conhecimento é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            errors["description"] = ["A descrição da base de conhecimento é obrigatória — é o texto que o agente usa para decidir quando consultá-la."];
        }

        return errors.Count > 0 ? errors : null;
    }
}
