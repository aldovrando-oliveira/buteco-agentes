using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeSync.Commands.RecordSyncResult;

/// <summary>
/// Aplica o resultado de um ciclo ao estado da base (D2). As regras moram na
/// entidade (<see cref="KnowledgeBase.RecordSyncSuccess"/> e
/// <see cref="KnowledgeBase.RecordSyncFailure"/>); aqui só o instante, que é o
/// relógio do <c>apps/api</c> no momento da gravação, nunca um campo do conector:
/// dois relógios, e uma gravação atrasada poderia mover o estado para trás.
/// </summary>
/// <remarks>
/// Não toca documentos nem o histórico. A base pode ser excluída entre a leitura e o
/// <c>UPDATE</c>, que então não acha a linha: 404, nunca 500
/// (exclusao-base-conhecimento, D6).
/// </remarks>
public sealed class RecordSyncResultCommandHandler(AppDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<RecordSyncResultCommand, RecordSyncResultResult>
{
    public async ValueTask<RecordSyncResultResult> Handle(RecordSyncResultCommand command, CancellationToken cancellationToken)
    {
        // Sem token de concorrência na base: o UPDATE só deixa de achar a linha se ela
        // foi apagada. Um impasse com a base existindo repete UMA vez.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RecordOnceAsync(command, cancellationToken);
            }
            catch (DbUpdateException exception) when (KnowledgeBaseWriteFailures.MayBeDeletedBase(exception))
            {
                if (await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken))
                {
                    return new RecordSyncResultResult(SyncedKnowledgeBaseLookup.NotFound, null);
                }

                if (attempt == 2)
                {
                    throw;
                }
            }
        }
    }

    private async Task<RecordSyncResultResult> RecordOnceAsync(RecordSyncResultCommand command, CancellationToken cancellationToken)
    {
        var knowledgeBase = await dbContext.KnowledgeBases
            .FirstOrDefaultAsync(knowledgeBase => knowledgeBase.Id == command.KnowledgeBaseId, cancellationToken);

        if (knowledgeBase is null)
        {
            return new RecordSyncResultResult(SyncedKnowledgeBaseLookup.NotFound, null);
        }

        if (knowledgeBase.ContentMode != KnowledgeBaseContentMode.Synced)
        {
            return new RecordSyncResultResult(SyncedKnowledgeBaseLookup.Manual, null);
        }

        var now = timeProvider.GetUtcNow();

        if (command.Succeeded)
        {
            knowledgeBase.RecordSyncSuccess(now, command.FolderName!, command.FolderUrl!, command.IgnoredFiles!);
        }
        else
        {
            knowledgeBase.RecordSyncFailure(now, command.ErrorCode!, command.ErrorDetail);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordSyncResultResult(SyncedKnowledgeBaseLookup.Synced, KnowledgeBaseResponse.FromEntity(knowledgeBase));
    }
}
