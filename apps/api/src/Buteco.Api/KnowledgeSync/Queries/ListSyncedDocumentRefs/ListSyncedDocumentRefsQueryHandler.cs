using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeSync.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeSync.Queries.ListSyncedDocumentRefs;

/// <summary>
/// Referências e versões dos documentos de uma base sincronizada (D9), sem o texto.
/// Sem esta rota o conector não teria como reconciliar sem guardar um mapeamento
/// próprio, que a #102 descarta.
/// </summary>
public sealed class ListSyncedDocumentRefsQueryHandler(AppDbContext dbContext)
    : IQueryHandler<ListSyncedDocumentRefsQuery, ListSyncedDocumentRefsResult>
{
    public async ValueTask<ListSyncedDocumentRefsResult> Handle(
        ListSyncedDocumentRefsQuery query, CancellationToken cancellationToken)
    {
        var lookup = await dbContext.LookupSyncedKnowledgeBaseAsync(query.KnowledgeBaseId, cancellationToken);
        if (lookup != SyncedKnowledgeBaseLookup.Synced)
        {
            return new ListSyncedDocumentRefsResult(lookup, null);
        }

        var documents = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Where(document => document.KnowledgeBaseId == query.KnowledgeBaseId)
            // ExternalRef é único por base; o desempate por Id é a regra da casa
            // (api-response-ordering), não necessidade deste critério.
            .OrderBy(document => document.ExternalRef)
            .ThenBy(document => document.Id)
            .Select(document => new SyncedDocumentRefResponse(document.ExternalRef!, document.ExternalVersion!, document.Id))
            .ToListAsync(cancellationToken);

        return new ListSyncedDocumentRefsResult(lookup, documents);
    }
}
