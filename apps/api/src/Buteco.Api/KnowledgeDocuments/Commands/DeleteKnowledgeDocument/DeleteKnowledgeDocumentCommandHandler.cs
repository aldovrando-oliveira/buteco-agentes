using Buteco.Api.Infrastructure;
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
/// <returns><c>true</c> se excluiu, <c>false</c> se não encontrou.</returns>
public sealed class DeleteKnowledgeDocumentCommandHandler(AppDbContext dbContext)
    : ICommandHandler<DeleteKnowledgeDocumentCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteKnowledgeDocumentCommand command, CancellationToken cancellationToken)
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
            return false;
        }

        dbContext.KnowledgeDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
