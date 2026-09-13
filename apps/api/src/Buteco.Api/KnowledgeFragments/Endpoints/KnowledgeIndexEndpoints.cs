using Buteco.Api.KnowledgeFragments.Queries.GetKnowledgeIndexDiagnostics;
using Buteco.Api.KnowledgeFragments.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.KnowledgeFragments.Endpoints;

/// <summary>
/// Recurso de leitura do <b>índice de conhecimento</b>, que não pertence a nenhuma
/// entidade do catálogo.
///
/// <para>
/// <b>Sem <c>MapGroup</c>, mapeada direto em <c>app</c></b> — molde de
/// <c>ProviderEndpoints</c>, o único precedente do repositório para recurso de
/// leitura que não é de um recurso (design.md, D1 e D4). Não vai sob
/// <c>/knowledge-bases</c> porque a proveniência não é da base: ficaria sugerindo
/// um <c>{id}</c> que a rota não tem e não pode ter.
/// </para>
///
/// <para>
/// <b>Autenticação vem por omissão, e o guarda é a <c>FallbackPolicy</c></b>
/// (design.md, D8) — não <c>ValidateRouteAuthenticationClassification</c>, que
/// varre <b>só</b> o caminho anônimo: rota com <c>AllowAnonymous</c> sem motivo
/// declarado, e allowlist defasada. Rota autenticada não aparece em nenhuma das
/// duas varreduras porque não há nada a declarar, e o desenho é seguro por padrão:
/// esquecer de declarar FECHA a rota. Por isso a rota NÃO entra na allowlist de
/// <c>Program.cs</c> — entrar lá reprovaria o boot, já que aquela lista é de rotas
/// que precisam estar anônimas. O 403 para o token de serviço de <c>apps/inbox</c>
/// vem de <c>ServiceScopeAuthorizationHandler</c>, também sem nada a escrever aqui.
/// </para>
/// </summary>
public static class KnowledgeIndexEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeIndexEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/knowledge-index/diagnostics", GetKnowledgeIndexDiagnosticsAsync);

        return app;
    }

    /// <summary>
    /// Array nu na raiz, não envelope (design.md, D4): mesma forma de
    /// <c>GET /providers</c> e <c>GET /knowledge-bases/indexing-summary</c>. Lista
    /// vazia é resposta válida e significa índice vazio — nunca 404, e nunca um
    /// item com campos zerados.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<KnowledgeIndexProvenanceResponse>>> GetKnowledgeIndexDiagnosticsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var provenance = await mediator.Send(new GetKnowledgeIndexDiagnosticsQuery(), cancellationToken);

        return TypedResults.Ok(provenance);
    }
}
