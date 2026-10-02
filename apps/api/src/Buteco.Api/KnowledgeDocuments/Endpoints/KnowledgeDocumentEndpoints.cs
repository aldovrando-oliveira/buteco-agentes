using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Commands.DeleteKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Commands.ReindexKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Queries.GetKnowledgeDocumentById;
using Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocumentEvents;
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

        // Idioma de POST /{id:guid}/activate: ação sobre um recurso existente.
        // É a ÚNICA entrada que reenfileira indexação sem o conteúdo ter mudado
        // — o bypass deliberado da regra do ContentHash, com o motivo em
        // design.md (D4) e no comentário de KnowledgeDocument.RequestReindex().
        group.MapPost("/{id:guid}/reindex", ReindexKnowledgeDocumentAsync);

        // Primeiro MapDelete do repositório. O padrão da casa é soft delete por
        // IsActive, e a divergência é deliberada, com o motivo em design.md
        // (D6): esse padrão foi formado para entidades de catálogo com vínculos
        // apontando para elas (um McpServer desativado continua referenciado
        // por AgentMcpServer). Documento é conteúdo — nada aponta para ele além
        // dos próprios fragmentos —, e o caso concreto que motiva a exclusão
        // (arquivo errado, ou com dado que não devia estar ali) é exatamente
        // aquele em que "continua no banco, invisível" é a resposta errada.
        group.MapDelete("/{id:guid}", DeleteKnowledgeDocumentAsync);

        // Histórico de documentos da base (design.md da change
        // historico-documentos-base, D8). Coleção própria, irmã de /documents e
        // não dentro dela: o evento de um documento excluído não é sub-recurso de
        // documento nenhum. Sem entrada na allowlist anônima nem no escopo de
        // serviço — só o operador lê.
        app.MapGet("/knowledge-bases/{knowledgeBaseId:guid}/document-events", ListKnowledgeDocumentEventsAsync);

        return app;
    }

    private static async Task<Results<Created<KnowledgeDocumentResponse>, NotFound, ValidationProblem>> CreateKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        CreateKnowledgeDocumentRequest request,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Title, request.SourceType, request.Content);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new CreateKnowledgeDocumentCommand(
            knowledgeBaseId, request.Title!, request.SourceType!, request.Content!, ReadAuthor(user));
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
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Title, request.SourceType, request.Content);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateKnowledgeDocumentCommand(
            knowledgeBaseId, id, request.Title!, request.SourceType!, request.Content!, ReadAuthor(user));
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

    /// <summary>
    /// Responde 200 com o documento já no estado novo, e não 202: a mudança de
    /// estado que a resposta descreve — <c>Pending</c>, contadores limpos —
    /// aconteceu de forma síncrona e completa. O que é assíncrono é a indexação,
    /// e o documento já diz isso no próprio <c>indexingStatus</c>. A tela
    /// re-renderiza a linha sem uma segunda leitura.
    /// </summary>
    private static async Task<Results<Ok<KnowledgeDocumentResponse>, NotFound>> ReindexKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var document = await mediator.Send(new ReindexKnowledgeDocumentCommand(knowledgeBaseId, id), cancellationToken);

        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(document);
    }

    private static async Task<Results<NoContent, NotFound>> DeleteKnowledgeDocumentAsync(
        Guid knowledgeBaseId,
        Guid id,
        ClaimsPrincipal user,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(
            new DeleteKnowledgeDocumentCommand(knowledgeBaseId, id, ReadAuthor(user)), cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<KnowledgeDocumentEventPageResponse>, NotFound, ValidationProblem>> ListKnowledgeDocumentEventsAsync(
        Guid knowledgeBaseId,
        string? cursor,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        KnowledgeDocumentEventCursor? after = null;
        if (cursor is not null)
        {
            if (!KnowledgeDocumentEventCursor.TryDecode(cursor, out var decoded))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["Cursor inválido: use o nextCursor devolvido pela página anterior."],
                });
            }

            after = decoded;
        }

        var page = await mediator.Send(new ListKnowledgeDocumentEventsQuery(knowledgeBaseId, after), cancellationToken);

        return page is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(page);
    }

    /// <summary>
    /// O autor de um evento do histórico é o subject do token, gravado como veio
    /// (design.md da change historico-documentos-base, D6). Lido pela MESMA forma
    /// de <see cref="ServiceScopeAuthorizationHandler"/>, sobre o claim que
    /// <see cref="OperatorTokenAuthenticationHandler"/> emite — uma forma só de ler
    /// o subject na base inteira.
    ///
    /// <para>
    /// O claim sempre existe aqui: a rota cai na <c>FallbackPolicy</c>
    /// autenticada. Se faltar mesmo assim, falha em vez de gravar autor vazio —
    /// afirmar autoria que não existe é pior que a escrita falhar.
    /// </para>
    /// </summary>
    private static string ReadAuthor(ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException(
            "Escrita de documento sem subject no token: a rota deveria ter exigido autenticação.");

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
