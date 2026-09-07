using Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Commands.DeleteKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Queries.GetKnowledgeDocumentById;
using Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocuments;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.KnowledgeDocuments.Endpoints;

public static class KnowledgeDocumentEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/knowledge-bases/{knowledgeBaseId:guid}/documents");

        group.MapPost("/", CreateKnowledgeDocumentAsync);
        group.MapGet("/", ListKnowledgeDocumentsAsync);
        group.MapGet("/{id:guid}", GetKnowledgeDocumentByIdAsync);
        group.MapPut("/{id:guid}", UpdateKnowledgeDocumentAsync);

        // Primeiro MapDelete do repositório. O padrão da casa é soft delete por
        // IsActive, e a divergência é deliberada, com o motivo em design.md
        // (D6): esse padrão foi formado para entidades de catálogo com vínculos
        // apontando para elas (um McpServer desativado continua referenciado
        // por AgentMcpServer). Documento é conteúdo — nada aponta para ele além
        // dos próprios fragmentos —, e o caso concreto que motiva a exclusão
        // (arquivo errado, ou com dado que não devia estar ali) é exatamente
        // aquele em que "continua no banco, invisível" é a resposta errada.
        group.MapDelete("/{id:guid}", DeleteKnowledgeDocumentAsync);

        return app;
    }

    private static async Task<Results<Created<KnowledgeDocumentResponse>, NotFound, ValidationProblem>> CreateKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        CreateKnowledgeDocumentRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Title, request.SourceType, request.Content);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new CreateKnowledgeDocumentCommand(
            knowledgeBaseId, request.Title!, request.SourceType!, request.Content!);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.KnowledgeBaseFound)
        {
            return TypedResults.NotFound();
        }

        if (result.ValidationErrors is not null)
        {
            return TypedResults.ValidationProblem(result.ValidationErrors);
        }

        return TypedResults.Created(
            $"/knowledge-bases/{knowledgeBaseId}/documents/{result.Document!.Id}", result.Document);
    }

    private static async Task<Results<Ok<IReadOnlyList<KnowledgeDocumentSummaryResponse>>, NotFound>> ListKnowledgeDocumentsAsync(
        Guid knowledgeBaseId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var documents = await mediator.Send(new ListKnowledgeDocumentsQuery(knowledgeBaseId), cancellationToken);

        return documents is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(documents);
    }

    private static async Task<Results<Ok<KnowledgeDocumentResponse>, NotFound>> GetKnowledgeDocumentByIdAsync(
        Guid knowledgeBaseId,
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var document = await mediator.Send(new GetKnowledgeDocumentByIdQuery(knowledgeBaseId, id), cancellationToken);

        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(document);
    }

    private static async Task<Results<Ok<KnowledgeDocumentResponse>, NotFound, ValidationProblem>> UpdateKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        Guid id,
        UpdateKnowledgeDocumentRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Title, request.SourceType, request.Content);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateKnowledgeDocumentCommand(
            knowledgeBaseId, id, request.Title!, request.SourceType!, request.Content!);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.Found)
        {
            return TypedResults.NotFound();
        }

        if (result.ValidationErrors is not null)
        {
            return TypedResults.ValidationProblem(result.ValidationErrors);
        }

        return TypedResults.Ok(result.Document!);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new DeleteKnowledgeDocumentCommand(knowledgeBaseId, id), cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>
    /// Só a forma do payload. O <c>sourceType</c> é validado contra os
    /// extratores registrados dentro do handler
    /// (<see cref="KnowledgeContentProcessor"/>), junto com a extração e o teto
    /// de tamanho — para que a ordem "extrai, depois valida o teto" fique num
    /// lugar só (design.md, D5).
    /// </summary>
    private static Dictionary<string, string[]>? ValidateShape(string? title, string? sourceType, string? content)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors["title"] = ["O título do documento é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(sourceType))
        {
            errors[KnowledgeContentProcessor.SourceTypeErrorKey] =
                [$"O tipo de origem é obrigatório. Valores aceitos: {string.Join(", ", KnowledgeSourceTypes.All)}."];
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            errors[KnowledgeContentProcessor.ContentErrorKey] = ["O conteúdo do documento é obrigatório e não pode ser vazio."];
        }

        return errors.Count > 0 ? errors : null;
    }
}
