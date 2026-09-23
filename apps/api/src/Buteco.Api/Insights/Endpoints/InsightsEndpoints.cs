using Buteco.Api.Insights.Queries.GetSystemInsights;
using Buteco.Api.Insights.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.Insights.Endpoints;

/// <summary>
/// <b>UMA rota, e não 27 nem seis.</b> Janela, fuso, tratamento de nulo e
/// "medindo desde" são um contrato só; reparti-los entre N rotas cria N lugares
/// onde o balde pode discordar, e a divergência entre dois cards da mesma tela é
/// exatamente o defeito que teste de rota isolada não pega (design.md, D4).
///
/// <para>
/// <b>Autenticação: nada a declarar, e o erro possível é o inverso do que se
/// imagina.</b> <c>apps/api</c> fecha por <c>FallbackPolicy</c>, e a validação de
/// startup varre SÓ o caminho anônimo — rota autenticada nova não aparece em
/// varredura nenhuma porque não há o que declarar. Acrescentar
/// <c>/insights/system</c> à allowlist de <c>Program.cs</c> REPROVARIA O BOOT:
/// aquela lista é de rotas que precisam estar anônimas.
/// </para>
///
/// <para>
/// Sem teto de intervalo, herdado do precedente das rotas de resumo de
/// <c>apps/inbox</c> — mas com o gatilho RENOMEADO, porque lá a rota devolve um
/// escalar e aqui devolve o agregado de 27 métricas: recalibrar na primeira
/// requisição de período que passar de 2 s contra o piloto (design.md, D6).
/// </para>
/// </summary>
public static class InsightsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/insights/system", GetSystemInsightsAsync);

        return app;
    }

    // from/to entram como string?, NÃO como DateTimeOffset?, e isso é decisão
    // herdada do precedente de apps/inbox: com o tipo de data na assinatura, um
    // valor malformado falha no BINDING, antes deste método rodar, e o ASP.NET
    // Core devolve um 400 com corpo próprio — forma diferente do
    // ValidationProblem de todo o resto da casa.
    private static async Task<Results<Ok<SystemInsightsResponse>, ValidationProblem>> GetSystemInsightsAsync(
        string? from,
        string? to,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (!InsightsPeriod.TryResolve(from, to, out var resolvedFrom, out var resolvedTo, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var insights = await mediator.Send(
            new GetSystemInsightsQuery(resolvedFrom, resolvedTo), cancellationToken);

        return TypedResults.Ok(insights);
    }
}
