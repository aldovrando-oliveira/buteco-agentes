using Buteco.Connectors.Options;
using Buteco.Connectors.Sync;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Endpoints;

/// <summary>
/// "Sincronizar agora" (design.md da change ciclo-de-sincronizacao, D9), só para o
/// operador, pela tabela de <see cref="Auth.ConnectorsSubjectAuthorizationHandler"/>.
/// </summary>
public static class SyncEndpoints
{
    public const string SyncNowPattern = "/connectors/knowledge-bases/{knowledgeBaseId:guid}/sync";

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(SyncNowPattern, SyncNowAsync);
        return app;
    }

    /// <summary>
    /// Confirma a base em <c>GET /sync/knowledge-bases</c>, sem guardar bases, e dispara o
    /// ciclo dela em segundo plano. <c>202</c> também quando um ciclo da base já está em
    /// curso: ele vai gravar o desfecho que a #107 espera, e um segundo não é disparado.
    /// </summary>
    private static async Task<Results<Accepted, ProblemHttpResult>> SyncNowAsync(
        Guid knowledgeBaseId,
        IOptions<ApiOptions> apiOptions,
        ISyncApiClient api,
        SyncRound round,
        IHostApplicationLifetime lifetime,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!apiOptions.Value.IsConfigured)
        {
            return Failure(SyncCodes.NotConfigured, StatusCodes.Status503ServiceUnavailable);
        }

        var bases = await api.ListKnowledgeBasesAsync(cancellationToken);
        switch (bases.Status)
        {
            case SyncApiStatus.Ok:
                break;

            case SyncApiStatus.Unreachable:
                return Failure(SyncCodes.ApiUnavailable, StatusCodes.Status503ServiceUnavailable);

            default:
                // Inclusive 401/403 do apps/api: repassados, deslogariam o operador no painel.
                loggerFactory.CreateLogger(typeof(SyncEndpoints)).LogError(
                    "apps/api respondeu fora do contrato ({Status}) ao confirmar a base {KnowledgeBaseId}.", bases.Status, knowledgeBaseId);
                return Failure(SyncCodes.ApiError, StatusCodes.Status502BadGateway);
        }

        // Manual não está na lista, e cai aqui junto com a inexistente.
        var knowledgeBase = bases.Value!.FirstOrDefault(candidate => candidate.Id == knowledgeBaseId);
        if (knowledgeBase is null)
        {
            return Failure(SyncCodes.KnowledgeBaseNotFound, StatusCodes.Status404NotFound);
        }

        // O ciclo vive além da requisição: o token é o da parada da aplicação.
        round.TryRunInBackground(knowledgeBase, lifetime.ApplicationStopping);
        return TypedResults.Accepted((string?)null);
    }

    // O texto exibido é do frontend; o título aqui é só para quem lê a resposta crua.
    private static string TitleFor(string code) => code switch
    {
        SyncCodes.NotConfigured => "A sincronização não está configurada neste processo.",
        SyncCodes.KnowledgeBaseNotFound => "Base sincronizada não encontrada.",
        SyncCodes.ApiUnavailable => "O apps/api não respondeu.",
        _ => "O apps/api respondeu fora do contrato.",
    };

    private static ProblemHttpResult Failure(string code, int status) =>
        TypedResults.Problem(
            title: TitleFor(code),
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
