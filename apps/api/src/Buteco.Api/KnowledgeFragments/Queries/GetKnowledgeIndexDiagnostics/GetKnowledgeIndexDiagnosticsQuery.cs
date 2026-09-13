using Buteco.Api.KnowledgeFragments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeFragments.Queries.GetKnowledgeIndexDiagnostics;

/// <summary>
/// Proveniência gravada no índice de conhecimento, numa requisição só.
///
/// <para>
/// <b>Sem parâmetro, e a ausência é o contrato</b> (design.md, D1): não existe
/// proveniência por base de conhecimento. Acrescentar um
/// <c>KnowledgeBaseId</c> aqui não tornaria a consulta mais precisa — tornaria a
/// resposta uma afirmação falsa, porque provedor, modelo e dimensão são únicos no
/// índice inteiro por construção do schema e da checagem de boot.
/// </para>
/// </summary>
public sealed record GetKnowledgeIndexDiagnosticsQuery
    : IQuery<IReadOnlyList<KnowledgeIndexProvenanceResponse>>;
