using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Entities;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Queries.GetKnowledgeBaseIndexingSummary;

/// <summary>
/// Duas consultas, sempre — uma pelas bases, uma pela agregação dos documentos.
/// <b>O custo não cresce com o número de bases</b>, que é a propriedade inteira
/// deste recurso: foi a contagem por base, a uma requisição cada, que fez a
/// etapa 5a-1 recusar as colunas do catálogo com 100+ bases declaradas.
/// </summary>
public sealed class GetKnowledgeBaseIndexingSummaryQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetKnowledgeBaseIndexingSummaryQuery, IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>>
{
    public async ValueTask<IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>> Handle(
        GetKnowledgeBaseIndexingSummaryQuery query, CancellationToken cancellationToken)
    {
        // Inclui bases inativas, como o catálogo faz. Filtrar por IsActive aqui
        // deixaria em branco exatamente as linhas que mais pedem atenção.
        //
        // Desempate estável: CreatedAt não é único — é atribuído no construtor
        // da entidade e dois registros podem compartilhar o instante —, então
        // ordenar só por ele deixa a ordem entre empatados a cargo do plano do
        // Postgres (api-response-ordering). Mesmo critério do catálogo, para que
        // as duas respostas sejam lidas na mesma ordem; o consumidor casa por
        // id, nunca por posição.
        var knowledgeBaseIds = await dbContext.KnowledgeBases
            .AsNoTracking()
            .OrderBy(knowledgeBase => knowledgeBase.CreatedAt)
            .ThenBy(knowledgeBase => knowledgeBase.Id)
            .Select(knowledgeBase => knowledgeBase.Id)
            .ToListAsync(cancellationToken);

        // UMA consulta agregada para todas as bases, no molde em lote de
        // ListAgentsQueryHandler — nunca contagem por base dentro do Select
        // abaixo, que seria N+1 e passaria despercebido porque o resultado sai
        // correto, só caro.
        var counts = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .GroupBy(document => document.KnowledgeBaseId)
            .Select(group => new
            {
                KnowledgeBaseId = group.Key,
                DocumentCount = group.Count(),
                IndexedCount = group.Count(document => document.IndexingStatus == KnowledgeIndexingStatus.Indexed),
                FailedCount = group.Count(document => document.IndexingStatus == KnowledgeIndexingStatus.Failed),
            })
            .ToDictionaryAsync(row => row.KnowledgeBaseId, cancellationToken);

        // A projeção final é sobre as BASES, não sobre os grupos. Projetar sobre
        // os grupos perderia em silêncio a base sem documento nenhum — o GroupBy
        // não emite grupo para quem não tem linha —, e é ela que precisa
        // aparecer com zeros para que "Nenhum" na tela seja uma contagem feita e
        // não uma ausência interpretada.
        return knowledgeBaseIds
            .Select(knowledgeBaseId => counts.TryGetValue(knowledgeBaseId, out var row)
                ? new KnowledgeBaseIndexingSummaryResponse(knowledgeBaseId, row.DocumentCount, row.IndexedCount, row.FailedCount)
                : new KnowledgeBaseIndexingSummaryResponse(knowledgeBaseId, 0, 0, 0))
            .ToList();
    }
}
