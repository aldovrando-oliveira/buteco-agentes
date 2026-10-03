using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeSync.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeSync.Queries.ListSyncedKnowledgeBases;

/// <summary>
/// Bases sincronizadas, <b>inclusive as inativas</b> (D8): desativar impede o uso
/// pelo agente, não a manutenção do conteúdo (#105).
/// </summary>
public sealed class ListSyncedKnowledgeBasesQueryHandler(AppDbContext dbContext)
    : IQueryHandler<ListSyncedKnowledgeBasesQuery, IReadOnlyList<SyncedKnowledgeBaseResponse>>
{
    public async ValueTask<IReadOnlyList<SyncedKnowledgeBaseResponse>> Handle(
        ListSyncedKnowledgeBasesQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.ContentMode == KnowledgeBaseContentMode.Synced)
            // Desempate por Id: CreatedAt não é único (api-response-ordering).
            .OrderBy(knowledgeBase => knowledgeBase.CreatedAt)
            .ThenBy(knowledgeBase => knowledgeBase.Id)
            .Select(knowledgeBase => new SyncedKnowledgeBaseResponse(
                knowledgeBase.Id,
                knowledgeBase.SyncProvider!,
                knowledgeBase.SyncFolderId!,
                knowledgeBase.SyncFolderName!,
                knowledgeBase.SyncFolderUrl!,
                knowledgeBase.IsActive))
            .ToListAsync(cancellationToken);
    }
}
