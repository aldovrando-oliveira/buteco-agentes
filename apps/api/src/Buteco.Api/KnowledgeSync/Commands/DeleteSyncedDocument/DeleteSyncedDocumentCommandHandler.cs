using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Entities;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeSync.Commands.DeleteSyncedDocument;

/// <summary>
/// Exclusão por <c>ExternalRef</c> (D8). Referência que a base não tem responde
/// como sucesso, sem evento: o estado pedido já vale, e a retentativa do conector
/// depois de uma falha de rede não vira erro. O 404 fica reservado para a base
/// inexistente, que a #105 trata como fim do ciclo daquela base — inclusive quando a
/// base é excluída entre a leitura e a gravação (exclusao-base-conhecimento, D6): o
/// 204 de referência ausente só vale com a base existindo.
/// </summary>
public sealed class DeleteSyncedDocumentCommandHandler(AppDbContext dbContext)
    : ICommandHandler<DeleteSyncedDocumentCommand, DeleteSyncedDocumentResult>
{
    public async ValueTask<DeleteSyncedDocumentResult> Handle(DeleteSyncedDocumentCommand command, CancellationToken cancellationToken)
    {
        var lookup = await dbContext.LookupSyncedKnowledgeBaseAsync(command.KnowledgeBaseId, cancellationToken);
        if (lookup != SyncedKnowledgeBaseLookup.Synced)
        {
            return new DeleteSyncedDocumentResult(lookup);
        }

        // ContentRevision é token de concorrência (D10), e o DELETE leva a revisão lida
        // no WHERE: um upsert concorrente entre a leitura e o SaveChanges faz a exclusão
        // falhar. Relê e exclui de novo UMA vez; uma segunda falha seguida não vira 500.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // Filtra por base E referência: a mesma referência pode existir em outra base.
            var document = await dbContext.KnowledgeDocuments
                .FirstOrDefaultAsync(
                    document => document.KnowledgeBaseId == command.KnowledgeBaseId && document.ExternalRef == command.ExternalRef,
                    cancellationToken);

            if (document is null)
            {
                // Referência ausente: 204 só com a base existindo. A releitura custa uma
                // consulta, e só neste caminho.
                return new DeleteSyncedDocumentResult(
                    await dbContext.LookupSyncedKnowledgeBaseAsync(command.KnowledgeBaseId, cancellationToken));
            }

            // Mesmo SaveChanges da exclusão (historico-documentos-base, D2).
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Deleted(document, command.Author));
            dbContext.KnowledgeDocuments.Remove(document);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return new DeleteSyncedDocumentResult(lookup);
            }
            catch (DbUpdateException exception) when (KnowledgeBaseWriteFailures.MayBeDeletedBase(exception))
            {
                if (await dbContext.KnowledgeBaseIsGoneAsync(command.KnowledgeBaseId, cancellationToken))
                {
                    return new DeleteSyncedDocumentResult(SyncedKnowledgeBaseLookup.NotFound);
                }
            }
        }

        return new DeleteSyncedDocumentResult(lookup, ConcurrentWriteConflict: true);
    }
}
