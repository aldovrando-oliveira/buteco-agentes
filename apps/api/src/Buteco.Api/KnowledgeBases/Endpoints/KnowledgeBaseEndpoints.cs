using Buteco.Api.KnowledgeBases.Commands.ActivateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.DeactivateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.DeleteKnowledgeBase;
using Buteco.Api.KnowledgeBases.Commands.UpdateKnowledgeBase;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseById;
using Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseIndexingSummary;
using Buteco.Api.KnowledgeBases.Queries.ListKnowledgeBases;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeSync;
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
        // Recurso próprio, e NÃO campos de contagem em KnowledgeBaseResponse
        // (design.md, D1) — é a decisão que alguém tentaria "corrigir" sem ver a
        // causa. Aquele record é construído em seis lugares, quatro deles
        // handlers de comando que não têm relação nenhuma com indexação, e
        // encarecer GET /knowledge-bases serviria uma única tela. Aqui o
        // catálogo continua sendo a consulta simples que é.
        //
        // Segmento literal antes do {id:guid}: a restrição de guid já exclui a
        // cadeia, e o roteamento classifica literal acima de parâmetro de
        // qualquer forma — nenhuma ordem de registro precisa ser garantida.
        group.MapGet("/indexing-summary", GetKnowledgeBaseIndexingSummaryAsync);
        group.MapGet("/{id:guid}", GetKnowledgeBaseByIdAsync);
        group.MapPut("/{id:guid}", UpdateKnowledgeBaseAsync);
        group.MapPost("/{id:guid}/activate", ActivateKnowledgeBaseAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateKnowledgeBaseAsync);

        // Base de conhecimento é a exceção à regra "catálogo só se desativa"
        // (design.md da change exclusao-base-conhecimento, D1): ninguém lê o passado
        // pelo id dela, e ela retém um recurso exclusivo — a pasta de uma base
        // sincronizada, que sem exclusão ficaria presa para sempre. Agent, McpServer e
        // Channel continuam só se desativando. Só base inativa (D2).
        group.MapDelete("/{id:guid}", DeleteKnowledgeBaseAsync);

        return app;
    }

    private static async Task<Results<Created<KnowledgeBaseResponse>, ValidationProblem, ProblemHttpResult>> CreateKnowledgeBaseAsync(
        CreateKnowledgeBaseRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var errors = ValidateShape(request.Name, request.Description)
            ?? ValidateContentMode(request.ContentMode)
            ?? ValidateSyncSource(request);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var synced = request.ContentMode == nameof(KnowledgeBaseContentMode.Synced);
        var result = await mediator.Send(
            new CreateKnowledgeBaseCommand(
                request.Name!,
                request.Description!,
                synced ? request.Provider : null,
                synced ? request.FolderId : null),
            cancellationToken);

        return result.Outcome switch
        {
            CreateKnowledgeBaseOutcome.Created =>
                TypedResults.Created($"/knowledge-bases/{result.KnowledgeBase!.Id}", result.KnowledgeBase),
            CreateKnowledgeBaseOutcome.FolderInUse => FolderInUse(result.ConflictingKnowledgeBaseId!.Value, result.ConflictingKnowledgeBaseName!),
            _ => FolderValidationFailed(result.ValidationFailure!),
        };
    }

    /// <summary>
    /// 409 de pasta em uso (design.md da change criacao-base-sincronizada, D5). A frase
    /// nomeia a base e diz que a pasta continua ocupada mesmo com a base inativa, e NÃO
    /// manda excluir a base. A rota de exclusão existe desde a #108, mas o botão no
    /// painel só chega com a #136, e a frase mandaria o operador a uma ação que a tela
    /// não oferece (design.md da change exclusao-base-conhecimento, D8). Ela muda junto
    /// com o botão, na #136, com o teste que afirma a ausência dessa instrução.
    /// </summary>
    private static ProblemHttpResult FolderInUse(Guid knowledgeBaseId, string knowledgeBaseName) =>
        TypedResults.Problem(
            title: "A pasta já é usada por outra base de conhecimento.",
            detail: $"A pasta já é usada pela base \"{knowledgeBaseName}\". Uma pasta pertence a uma base só, e continua ocupada mesmo com a base inativa.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "folder-in-use",
                ["knowledgeBaseId"] = knowledgeBaseId,
                ["knowledgeBaseName"] = knowledgeBaseName,
            });

    /// <summary>
    /// Falha da validação no apps/connectors: o código e o detalhe como vieram, com o
    /// status que o cliente decidiu (D2 e D3). O título é fixo e genérico, só para quem
    /// lê a resposta crua; o painel escolhe a mensagem pelo código (#106).
    /// </summary>
    private static ProblemHttpResult FolderValidationFailed(KnowledgeSync.Connectors.ConnectorsFolderResult failure) =>
        TypedResults.Problem(
            title: "A pasta não pôde ser validada pelo apps/connectors.",
            detail: failure.FailureDetail,
            statusCode: failure.FailureStatus,
            extensions: new Dictionary<string, object?> { ["code"] = failure.FailureCode });

    private static async Task<Ok<IReadOnlyList<KnowledgeBaseResponse>>> ListKnowledgeBasesAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var knowledgeBases = await mediator.Send(new ListKnowledgeBasesQuery(), cancellationToken);

        return TypedResults.Ok(knowledgeBases);
    }

    private static async Task<Ok<IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>>> GetKnowledgeBaseIndexingSummaryAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var summary = await mediator.Send(new GetKnowledgeBaseIndexingSummaryQuery(), cancellationToken);

        return TypedResults.Ok(summary);
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

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteKnowledgeBaseAsync(
        Guid id,
        HttpContext httpContext,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeleteKnowledgeBaseCommand(id), cancellationToken);

        return result switch
        {
            DeleteKnowledgeBaseResult.Deleted => TypedResults.NoContent(),
            DeleteKnowledgeBaseResult.Active => KnowledgeBaseActive(),
            DeleteKnowledgeBaseResult.ConcurrentWrite => DeletionContended(httpContext),
            _ => TypedResults.NotFound(),
        };
    }

    /// <summary>
    /// 409 de base ativa (D2, D11): o operador tem permissão e a rota oferece o verbo;
    /// o que impede é o estado da base. O painel escolhe o texto pelo código.
    /// </summary>
    private static ProblemHttpResult KnowledgeBaseActive() =>
        TypedResults.Problem(
            title: "A base de conhecimento está ativa.",
            detail: "Só uma base inativa pode ser excluída. Desative a base antes de excluí-la.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["code"] = "knowledge-base-active" });

    /// <summary>Dois impasses seguidos (D6, D11): nada foi apagado, e repetir resolve.</summary>
    private static ProblemHttpResult DeletionContended(HttpContext httpContext)
    {
        httpContext.Response.Headers.RetryAfter = "1";
        return TypedResults.Problem(
            title: "Outra escrita impediu a exclusão desta base duas vezes seguidas. Nada foi excluído; repita a operação.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
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
    /// Omitido, <c>Manual</c> ou <c>Synced</c> (design.md da change
    /// catalogo-base-sincronizada, D11; a recusa de <c>Synced</c> saiu com a #104). Valor
    /// desconhecido é recusado: ignorá-lo criaria com 201 uma coisa diferente da pedida.
    /// </summary>
    private static Dictionary<string, string[]>? ValidateContentMode(string? contentMode)
    {
        if (contentMode is null ||
            contentMode == nameof(KnowledgeBaseContentMode.Manual) ||
            contentMode == nameof(KnowledgeBaseContentMode.Synced))
        {
            return null;
        }

        return new Dictionary<string, string[]>
        {
            ["contentMode"] = [$"Tipo de conteúdo desconhecido. Valores aceitos: {nameof(KnowledgeBaseContentMode.Manual)} e {nameof(KnowledgeBaseContentMode.Synced)}."],
        };
    }

    /// <summary>
    /// Forma da origem, antes de qualquer chamada ao apps/connectors (design.md da
    /// change criacao-base-sincronizada, D6).
    ///
    /// <para>
    /// Em <c>Manual</c>, <c>provider</c> e <c>folderId</c> são proibidos, e todo valor
    /// não nulo é recusado, inclusive vazio: o cliente achou que estava criando outra
    /// coisa. <c>null</c> conta como ausente, porque a desserialização não distingue
    /// propriedade ausente de nula.
    /// </para>
    ///
    /// <para>
    /// Em <c>Synced</c>, o provedor tem a forma de código (vai no caminho da URL do
    /// apps/connectors e fecha <c>../</c> sem lista fechada de provedores) e o id da
    /// pasta é não vazio, até 256 caracteres, aceito como veio.
    /// </para>
    /// </summary>
    private static Dictionary<string, string[]>? ValidateSyncSource(CreateKnowledgeBaseRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.ContentMode != nameof(KnowledgeBaseContentMode.Synced))
        {
            if (request.Provider is not null)
            {
                errors["provider"] = ["Base manual não tem provedor. Para acompanhar uma pasta, crie a base com contentMode Synced."];
            }

            if (request.FolderId is not null)
            {
                errors["folderId"] = ["Base manual não tem pasta. Para acompanhar uma pasta, crie a base com contentMode Synced."];
            }

            return errors.Count > 0 ? errors : null;
        }

        if (!SyncCode.IsValid(request.Provider))
        {
            errors["provider"] = [$"O provedor é obrigatório na base sincronizada, e precisa ser {SyncCode.ShapeDescription}."];
        }

        if (string.IsNullOrWhiteSpace(request.FolderId))
        {
            errors["folderId"] = ["O id da pasta é obrigatório na base sincronizada."];
        }
        else if (request.FolderId.Length > MaxFolderIdLength)
        {
            errors["folderId"] = [$"O id da pasta tem no máximo {MaxFolderIdLength} caracteres."];
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>
    /// Teto contra abuso, não regra do provedor: os ids de pasta medidos na etapa 0 têm
    /// entre 33 e 51 caracteres (D6).
    /// </summary>
    private const int MaxFolderIdLength = 256;

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
