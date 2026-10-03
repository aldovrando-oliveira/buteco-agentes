using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeSync;

/// <summary>
/// As três respostas que toda rota de <c>/sync</c> dá antes de fazer qualquer coisa
/// (D8): base inexistente é 404 (a #105 encerra o ciclo daquela base), base manual é
/// 409, e só a base sincronizada segue.
/// </summary>
public enum SyncedKnowledgeBaseLookup
{
    Synced,
    NotFound,
    Manual,
}

public static class SyncedKnowledgeBaseLookupExtensions
{
    public static async Task<SyncedKnowledgeBaseLookup> LookupSyncedKnowledgeBaseAsync(
        this AppDbContext dbContext, Guid knowledgeBaseId, CancellationToken cancellationToken)
    {
        var contentMode = await dbContext.KnowledgeBases
            .Where(knowledgeBase => knowledgeBase.Id == knowledgeBaseId)
            .Select(knowledgeBase => (KnowledgeBaseContentMode?)knowledgeBase.ContentMode)
            .FirstOrDefaultAsync(cancellationToken);

        return contentMode switch
        {
            null => SyncedKnowledgeBaseLookup.NotFound,
            KnowledgeBaseContentMode.Synced => SyncedKnowledgeBaseLookup.Synced,
            _ => SyncedKnowledgeBaseLookup.Manual,
        };
    }
}
