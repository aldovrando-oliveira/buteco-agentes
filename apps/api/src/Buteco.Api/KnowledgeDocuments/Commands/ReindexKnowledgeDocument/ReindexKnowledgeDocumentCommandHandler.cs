using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.ReindexKnowledgeDocument;

public sealed class ReindexKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    IKnowledgeIndexingJobPublisher indexingPublisher)
    : ICommandHandler<ReindexKnowledgeDocumentCommand, KnowledgeDocumentResponse?>
{
    public async ValueTask<KnowledgeDocumentResponse?> Handle(
        ReindexKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // Filtra por KnowledgeBaseId E Id, nunca só por Id: sem isso seria
        // possível reindexar documento de outra base pelo caminho errado —
        // mesma regra de UpdateKnowledgeDocumentCommandHandler.
        //
        // Base inativa não é recusada, e isso é decisão: desativar impede o uso
        // pelo agente, não a manutenção do conteúdo (design.md, D6). Reindexar
        // os documentos antes de reativar a base é exatamente o que um operador
        // faz.
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(
                document => document.Id == command.Id && document.KnowledgeBaseId == command.KnowledgeBaseId,
                cancellationToken);

        if (document is null)
        {
            return null;
        }

        // Aceita em QUALQUER estado, inclusive Indexing (design.md, D5). Rota
        // não impõe estado de tela, e o estado pode virar entre a leitura da
        // tela e o POST; um 409 converteria uma corrida benigna num erro que o
        // operador teria de interpretar, sem eliminar a corrida.
        //
        // As duas exceções que esta chamada exerce — ao ContentHash e ao
        // significado de IndexingAttempts — estão ditas no comentário de
        // RequestReindex(), inclusive por que anular ContentHash aqui seria
        // armadilha.
        document.RequestReindex();
        await dbContext.SaveChangesAsync(cancellationToken);

        // Publica DEPOIS do SaveChanges, nunca antes — mesma ordem e mesma forma
        // de Create e Update (design.md, V4). Attempt fica no default 1: é a
        // abertura de uma rodada nova, e o limite de execuções viaja na
        // mensagem, não na coluna do documento.
        //
        // ContentRevision vai inalterada, de propósito: o conteúdo não mudou, e
        // ela é o token de descarte do consumidor.
        await indexingPublisher.PublishAsync(
            new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);

        return KnowledgeDocumentResponse.FromEntity(document);
    }
}
