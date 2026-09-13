using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeFragments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeFragments.Queries.GetKnowledgeIndexDiagnostics;

/// <summary>
/// <b>Uma consulta, sempre</b> — agregação no banco, nada em memória. O custo não
/// cresce com o número de bases nem de documentos, que é a propriedade inteira
/// deste recurso.
///
/// <para>
/// <b>Custo medido</b> (design.md), contra <c>pgvector/pgvector:pg18</c> com
/// 150.000 fragmentos em 50 bases: a varredura lê <b>só o heap</b> — 234 MB,
/// 30.000 páginas — porque o <c>attstorage</c> da coluna de vetor é
/// <c>EXTERNAL</c> e os 2,4 GB de vetor ficam em TOAST, fora do caminho. De
/// 0,24 s a 0,82 s, e <b>sem sujar o cache</b>: o <i>ring buffer</i> de leitura em
/// massa deixou 282 páginas em <c>shared_buffers</c> das 30.000 lidas, então esta
/// rota não evicta o cache que a busca de conhecimento usa.
/// </para>
///
/// <para>
/// <b>NÃO existe índice nas três colunas, e isso é decisão com gatilho</b>
/// (design.md, D6), no mesmo molde do índice HNSW que também não existe:
/// <c>CREATE INDEX CONCURRENTLY</c> quando o p95 da rota passar de 500 ms ou o
/// heap de <c>knowledge_fragments</c> passar de 500 MB. Medido, o índice custaria
/// 1,08 MB para 150.000 linhas — minúsculo porque a deduplicação do btree colapsa
/// valores idênticos, e o invariante que torna a varredura cara é o mesmo que
/// torna o índice barato.
/// </para>
///
/// <para>
/// <b>E NÃO trocar por <c>LIMIT 1</c>.</b> Leria três páginas em vez de 30.000 e
/// seria cego ao único estado que motiva a tela: afirmaria a proveniência do
/// índice inteiro a partir de uma linha, e a corrupção de duas combinações ficaria
/// invisível. Medido também que <c>LIMIT 2</c> não ajuda — o
/// <c>HashAggregate</c> consome toda a entrada antes de emitir a primeira linha,
/// então a agregação completa não é desperdício a otimizar.
/// </para>
/// </summary>
public sealed class GetKnowledgeIndexDiagnosticsQueryHandler(AppDbContext dbContext)
    : IQueryHandler<GetKnowledgeIndexDiagnosticsQuery, IReadOnlyList<KnowledgeIndexProvenanceResponse>>
{
    public async ValueTask<IReadOnlyList<KnowledgeIndexProvenanceResponse>> Handle(
        GetKnowledgeIndexDiagnosticsQuery query, CancellationToken cancellationToken)
    {
        // A ordenação é da CONSULTA, nunca comparação em memória no processo
        // (api-response-ordering). Motivo medido lá, não presumido: a collation do
        // PostgreSQL e o comparador de string do .NET discordam de fato para nomes
        // que diferem em caixa e pontuação, e a cultura do processo não é fixada
        // em lugar nenhum deste repositório.
        //
        // NÃO falta desempate por identificador: as três colunas SÃO o
        // identificador da combinação — são a própria chave de agrupamento, únicas
        // por construção. Não há empate possível para desempatar, e acrescentar um
        // quarto critério seria ordenar por algo que não distingue nada.
        return await dbContext.KnowledgeFragments
            .AsNoTracking()
            .GroupBy(fragment => new
            {
                fragment.EmbeddingProvider,
                fragment.EmbeddingModel,
                fragment.EmbeddingDimensions,
            })
            .OrderBy(group => group.Key.EmbeddingProvider)
            .ThenBy(group => group.Key.EmbeddingModel)
            .ThenBy(group => group.Key.EmbeddingDimensions)
            // LongCount, não Count: count(*) do PostgreSQL é bigint, e Count()
            // acrescentaria um cast para int sem nenhum ganho.
            .Select(group => new KnowledgeIndexProvenanceResponse(
                group.Key.EmbeddingProvider,
                group.Key.EmbeddingModel,
                group.Key.EmbeddingDimensions,
                group.LongCount()))
            .ToListAsync(cancellationToken);
    }
}
