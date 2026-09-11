using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseIndexingSummary;

/// <summary>
/// Estado de indexação agregado de <b>todas</b> as bases, numa requisição só.
/// Sem parâmetro: o consumidor é o catálogo, que quer o conjunto inteiro.
/// </summary>
public sealed record GetKnowledgeBaseIndexingSummaryQuery
    : IQuery<IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>>;
