using System.Security.Claims;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Endpoints;
using Buteco.Api.KnowledgeSync.Commands.DeleteSyncedDocument;
using Buteco.Api.KnowledgeSync.Commands.RecordSyncResult;
using Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;
using Buteco.Api.KnowledgeSync.Queries.ListSyncedDocumentRefs;
using Buteco.Api.KnowledgeSync.Queries.ListSyncedKnowledgeBases;
using Buteco.Api.KnowledgeSync.Requests;
using Buteco.Api.KnowledgeSync.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.KnowledgeSync.Endpoints;

/// <summary>
/// Rotas de serviço da sincronização (design.md da change
/// catalogo-base-sincronizada, D8), consumidas pelo <c>apps/connectors</c> com o
/// subject <c>service:connectors</c>. Superfície própria, separada das rotas do
/// operador: as regras são opostas por subject (o operador recebe 409 onde o
/// serviço escreve).
/// </summary>
/// <remarks>
/// As cinco rotas estão na lista de <c>service:connectors</c> em
/// <see cref="Auth.ServiceScopeAuthorizationHandler"/>, conferida no boot. O
/// operador também passa nelas, como em toda rota.
/// </remarks>
public static class KnowledgeSyncEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync/knowledge-bases");

group.MapGet("/", ListSyncedKnowledgeBasesAsync);
        group.MapGet("/{knowledgeBaseId:guid}/documents", ListSyncedDocumentRefsAsync);
        group.MapPut("/{knowledgeBaseId:guid}/documents", UpsertSyncedDocumentAsync);
        group.MapDelete("/{knowledgeBaseId:guid}/documents", DeleteSyncedDocumentAsync);
        group.MapPost("/{knowledgeBaseId:guid}/sync-results", RecordSyncResultAsync);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<SyncedKnowledgeBaseResponse>>> ListSyncedKnowledgeBasesAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var knowledgeBases = await mediator.Send(new ListSyncedKnowledgeBasesQuery(), cancellationToken);

        return TypedResults.Ok(knowledgeBases);
    }

    private static async Task<Results<Ok<IReadOnlyList<SyncedDocumentRefResponse>>, NotFound, ProblemHttpResult>> ListSyncedDocumentRefsAsync(
        Guid knowledgeBaseId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListSyncedDocumentRefsQuery(knowledgeBaseId), cancellationToken);

        return result.Lookup switch
        {
            SyncedKnowledgeBaseLookup.Synced => TypedResults.Ok(result.Documents!),
            SyncedKnowledgeBaseLookup.Manual => ManualKnowledgeBaseConflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<UpsertSyncedDocumentResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpsertSyncedDocumentAsync(
        Guid knowledgeBaseId,
        UpsertSyncedDocumentRequest request,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Os três campos que o operador também envia passam pela mesma validação de
        // forma; os dois da sincronização vêm em seguida.
        var errors = KnowledgeDocumentEndpoints.ValidateShape(request.Title, request.SourceType, request.Content)
            ?? new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.ExternalRef))
        {
            errors["externalRef"] = ["A referência externa do arquivo é obrigatória."];
        }

        if (string.IsNullOrWhiteSpace(request.ExternalVersion))
        {
            errors["externalVersion"] = ["O marcador de versão do provedor é obrigatório."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var command = new UpsertSyncedDocumentCommand(
            knowledgeBaseId,
            request.ExternalRef!,
            request.ExternalVersion!,
            request.Title!,
            request.SourceType!,
            request.Content!,
            KnowledgeDocumentEndpoints.ReadAuthor(user));
        var result = await mediator.Send(command, cancellationToken);

        if (result.Lookup == SyncedKnowledgeBaseLookup.NotFound)
        {
            return TypedResults.NotFound();
        }

        if (result.Lookup == SyncedKnowledgeBaseLookup.Manual)
        {
            return ManualKnowledgeBaseConflict();
        }

        if (result.ContentRefusal is not null)
        {
            return KnowledgeDocumentEndpoints.ContentRefused(result.ContentRefusal);
        }

        if (result.ConcurrentWriteConflict)
        {
            return KnowledgeDocumentEndpoints.ConcurrentWriteConflict(httpContext);
        }

        return TypedResults.Ok(result.Response!);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> DeleteSyncedDocumentAsync(
        Guid knowledgeBaseId,
        string? externalRef,
        ClaimsPrincipal user,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["externalRef"] = ["A referência externa do arquivo é obrigatória, na query (?externalRef=...)."],
            });
        }

        var result = await mediator.Send(
            new DeleteSyncedDocumentCommand(knowledgeBaseId, externalRef, KnowledgeDocumentEndpoints.ReadAuthor(user)),
            cancellationToken);

        if (result.ConcurrentWriteConflict)
        {
            return KnowledgeDocumentEndpoints.ConcurrentWriteConflict(httpContext);
        }

        return result.Lookup switch
        {
            SyncedKnowledgeBaseLookup.Synced => TypedResults.NoContent(),
            SyncedKnowledgeBaseLookup.Manual => ManualKnowledgeBaseConflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<KnowledgeBaseResponse>, NotFound, ValidationProblem, ProblemHttpResult>> RecordSyncResultAsync(
        Guid knowledgeBaseId,
        RecordSyncResultRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var (command, errors) = BuildRecordSyncResultCommand(knowledgeBaseId, request);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await mediator.Send(command!, cancellationToken);

        return result.Lookup switch
        {
            SyncedKnowledgeBaseLookup.Synced => TypedResults.Ok(result.KnowledgeBase!),
            SyncedKnowledgeBaseLookup.Manual => ManualKnowledgeBaseConflict(),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>
    /// Validação de forma por desfecho (D1, D2, D8). Campos do outro desfecho são
    /// recusados em vez de ignorados: é contrato entre apps, e ignorar esconderia um
    /// defeito do conector.
    /// </summary>
    private static (RecordSyncResultCommand? Command, Dictionary<string, string[]>? Errors) BuildRecordSyncResultCommand(
        Guid knowledgeBaseId, RecordSyncResultRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Outcome == "Succeeded")
        {
            if (string.IsNullOrWhiteSpace(request.FolderName))
            {
                errors["folderName"] = ["O nome da pasta é obrigatório num ciclo bem-sucedido."];
            }

            if (string.IsNullOrWhiteSpace(request.FolderUrl))
            {
                errors["folderUrl"] = ["A URL da pasta é obrigatória num ciclo bem-sucedido."];
            }

            if (request.IgnoredFiles is null)
            {
                errors["ignoredFiles"] = ["A lista de arquivos ignorados é obrigatória num ciclo bem-sucedido, vazia se nenhum foi ignorado."];
            }

            if (request.Error is not null)
            {
                errors["error"] = ["Um ciclo bem-sucedido não tem erro."];
            }

            var ignoredFiles = ValidateIgnoredFiles(request.IgnoredFiles ?? [], errors);

            return errors.Count > 0
                ? (null, errors)
                : (new RecordSyncResultCommand(knowledgeBaseId, true, request.FolderName, request.FolderUrl, ignoredFiles, null, null), null);
        }

        if (request.Outcome == "Failed")
        {
            if (request.Error is null)
            {
                errors["error"] = ["O erro é obrigatório num ciclo com falha."];
            }
            else if (!SyncCode.IsValid(request.Error.Code))
            {
                errors["error.code"] = [$"O código do erro precisa ser {SyncCode.ShapeDescription}."];
            }

            if (request.FolderName is not null || request.FolderUrl is not null)
            {
                errors["folderName"] = ["Um ciclo com falha não atualiza nome nem URL da pasta."];
            }

            if (request.IgnoredFiles is not null)
            {
                errors["ignoredFiles"] = ["Um ciclo com falha não atualiza a lista de arquivos ignorados."];
            }

            return errors.Count > 0
                ? (null, errors)
                : (new RecordSyncResultCommand(knowledgeBaseId, false, null, null, null, request.Error!.Code, request.Error.Detail), null);
        }

        errors["outcome"] = ["O desfecho é obrigatório: Succeeded ou Failed."];
        return (null, errors);
    }

    private static List<KnowledgeBaseSyncIgnoredFile> ValidateIgnoredFiles(
        IReadOnlyList<SyncIgnoredFileRequest?> files, Dictionary<string, string[]> errors)
    {
        var accepted = new List<KnowledgeBaseSyncIgnoredFile>();
        var seenRefs = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var key = $"ignoredFiles[{index}]";

            if (file is null || string.IsNullOrWhiteSpace(file.ExternalRef) || string.IsNullOrWhiteSpace(file.Name))
            {
                errors[key] = ["Cada arquivo ignorado precisa de externalRef e name."];
                continue;
            }

            if (!SyncCode.IsValid(file.Code))
            {
                errors[$"{key}.code"] = [$"O motivo precisa ser {SyncCode.ShapeDescription}."];
                continue;
            }

            // Comparação como veio, a mesma do ExternalRef no banco.
            if (!seenRefs.Add(file.ExternalRef))
            {
                errors[$"{key}.externalRef"] = ["Referência repetida na lista de arquivos ignorados."];
                continue;
            }

            accepted.Add(new KnowledgeBaseSyncIgnoredFile(file.ExternalRef, file.Name, file.Code!, file.Detail));
        }

        return accepted;
    }

    /// <summary>
    /// 409 nas rotas de <c>/sync</c> para base manual (D7): os documentos dela são
    /// do operador, e ela não tem estado de sincronização.
    /// </summary>
    private static ProblemHttpResult ManualKnowledgeBaseConflict() =>
        TypedResults.Problem(
            title: "Esta base é manual: os documentos são do operador, e a base não tem sincronização.",
            statusCode: StatusCodes.Status409Conflict);
}
