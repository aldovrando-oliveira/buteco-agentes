using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocuments;

/// <summary>
/// Devolve <c>null</c> quando a base não existe (vira 404 no endpoint); lista
/// vazia quando a base existe e não tem documentos (200, nunca 404).
/// </summary>
public sealed class ListKnowledgeDocumentsQueryHandler(AppDbContext dbContext)
    : IQueryHandler<ListKnowledgeDocumentsQuery, IReadOnlyList<KnowledgeDocumentSummaryResponse>?>
{
    public async ValueTask<IReadOnlyList<KnowledgeDocumentSummaryResponse>?> Handle(
        ListKnowledgeDocumentsQuery query, CancellationToken cancellationToken)
    {
        var knowledgeBaseExists = await dbContext.KnowledgeBases
            .AnyAsync(knowledgeBase => knowledgeBase.Id == query.KnowledgeBaseId, cancellationToken);

        if (!knowledgeBaseExists)
        {
            return null;
        }

        // Projeção sem ExtractedText: o texto nunca sai do banco (design.md,
        // D14). ContentLengthBytes vem da coluna gerada, não de
        // ExtractedText.Length — que traduziria para length() e contaria
        // caracteres, divergindo da unidade do teto.
        return await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Where(document => document.KnowledgeBaseId == query.KnowledgeBaseId)
            // Desempate estável: CreatedAt não é único — é atribuído no
            // construtor da entidade e dois registros podem compartilhar o
            // instante —, então ordenar só por ele deixa a ordem entre
            // empatados a cargo do plano do Postgres (api-response-ordering).
            .OrderBy(document => document.CreatedAt)
            .ThenBy(document => document.Id)
            .Select(document => new KnowledgeDocumentSummaryResponse(
                document.Id,
                document.KnowledgeBaseId,
                document.Title,
                document.SourceType,
                document.ContentLengthBytes,
                document.IndexingStatus,
                document.IndexedAt,
                document.FailureReason,
                document.ContentRevision,
                document.FragmentCount,
                document.IndexingAttempts,
                document.LastAttemptAt,
                document.CreatedAt,
                document.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
