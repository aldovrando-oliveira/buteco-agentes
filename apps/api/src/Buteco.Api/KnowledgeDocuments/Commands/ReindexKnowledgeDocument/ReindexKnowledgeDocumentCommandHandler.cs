using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.ReindexKnowledgeDocument;

public sealed class ReindexKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    IKnowledgeIndexingJobPublisher indexingPublisher)
    : ICommandHandler<ReindexKnowledgeDocumentCommand, ReindexKnowledgeDocumentResult>
{
    // Reindexar continua liberado em base sincronizada (catalogo-base-sincronizada, D7):
    // não muda conteúdo nem título, e por isso não consulta o tipo da base.
    public async ValueTask<ReindexKnowledgeDocumentResult> Handle(
        ReindexKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // ContentRevision é token de concorrência (catalogo-base-sincronizada, D10), e o
        // UPDATE desta gravação leva a revisão lida no WHERE: uma edição concorrente de
        // conteúdo entre a leitura e o SaveChanges faz a reindexação falhar com
        // DbUpdateConcurrencyException. Relê e reaplica UMA vez — a mensagem precisa
        // levar a revisão corrente, não a lida, ou o consumidor a descarta; uma
        // segunda falha seguida não vira 500.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var result = await TryOnceAsync(command, cancellationToken);
            if (result is not null)
            {
                return result;
            }

            dbContext.ChangeTracker.Clear();
        }

        return new ReindexKnowledgeDocumentResult(null, ConcurrentWriteConflict: true);
    }

    /// <returns>O resultado, ou <c>null</c> quando perdeu para uma escrita concorrente.</returns>
    private async Task<ReindexKnowledgeDocumentResult?> TryOnceAsync(
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
            return new ReindexKnowledgeDocumentResult(null);
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
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }

        // Publica DEPOIS do SaveChanges, nunca antes — mesma ordem e mesma forma
        // de Create e Update (design.md, V4). Attempt fica no default 1: é a
        // abertura de uma rodada nova, e o limite de execuções viaja na
        // mensagem, não na coluna do documento.
        //
        // ContentRevision vai inalterada, de propósito: o conteúdo não mudou, e
        // ela é o token de descarte do consumidor.
        await indexingPublisher.PublishAsync(
            new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);

        return new ReindexKnowledgeDocumentResult(KnowledgeDocumentResponse.FromEntity(document));
    }
}
