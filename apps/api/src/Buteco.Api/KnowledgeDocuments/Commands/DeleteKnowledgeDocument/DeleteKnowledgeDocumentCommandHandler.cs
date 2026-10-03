using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.DeleteKnowledgeDocument;

/// <summary>
/// Exclusão real — a única do repositório (design.md, D6). Documento é
/// conteúdo, não entidade de catálogo com vínculos apontando para ela, e o caso
/// de uso concreto (arquivo errado, ou com dado que não devia estar ali) é
/// exatamente aquele em que manter o registro invisível no banco é a resposta
/// errada.
/// </summary>
/// <returns>
/// Excluído, não encontrado, ou recusado porque a base é sincronizada
/// (catalogo-base-sincronizada, D7).
/// </returns>
public sealed class DeleteKnowledgeDocumentCommandHandler(AppDbContext dbContext)
    : ICommandHandler<DeleteKnowledgeDocumentCommand, DeleteKnowledgeDocumentResult>
{
    public async ValueTask<DeleteKnowledgeDocumentResult> Handle(DeleteKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // O 409 vem ANTES de procurar o documento (catalogo-base-sincronizada, D7):
        // o operador não exclui nada em base sincronizada, exista o documento ou
        // não. Base inexistente continua 404.
        var contentMode = await dbContext.KnowledgeBases
            .Where(knowledgeBase => knowledgeBase.Id == command.KnowledgeBaseId)
            .Select(knowledgeBase => (KnowledgeBaseContentMode?)knowledgeBase.ContentMode)
            .FirstOrDefaultAsync(cancellationToken);

        if (contentMode is null)
        {
            return DeleteKnowledgeDocumentResult.NotFound;
        }

        if (contentMode == KnowledgeBaseContentMode.Synced)
        {
            return DeleteKnowledgeDocumentResult.SyncedKnowledgeBase;
        }

        // ContentRevision é token de concorrência (catalogo-base-sincronizada, D10), e o
        // DELETE do EF também leva a revisão lida no WHERE: uma edição concorrente
        // entre a leitura e o SaveChanges faz a exclusão falhar com
        // DbUpdateConcurrencyException. Relê e exclui de novo UMA vez, com o título
        // atual no evento; uma segunda falha seguida não vira 500.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            // Filtra por KnowledgeBaseId E Id, nunca só por Id. Aqui é o cenário
            // mais caro de errar de toda a change: a exclusão é irreversível, e
            // resolver só por Id permitiria apagar documento de outra base pelo
            // caminho errado.
            var document = await dbContext.KnowledgeDocuments
                .FirstOrDefaultAsync(
                    document => document.Id == command.Id && document.KnowledgeBaseId == command.KnowledgeBaseId,
                    cancellationToken);

            if (document is null)
            {
                return DeleteKnowledgeDocumentResult.NotFound;
            }

            // O evento é montado ANTES do Remove, com o título que o documento tem
            // agora, e sobrevive a ele: não há FK de evento para documento
            // (historico-documentos-base, D1). Mesmo SaveChanges da exclusão (D2).
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Deleted(document, command.Author));
            dbContext.KnowledgeDocuments.Remove(document);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return DeleteKnowledgeDocumentResult.Deleted;
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        return DeleteKnowledgeDocumentResult.ConcurrentWrite;
    }
}
