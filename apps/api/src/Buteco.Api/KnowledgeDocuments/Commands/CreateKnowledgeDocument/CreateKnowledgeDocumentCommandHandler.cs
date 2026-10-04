using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;

public sealed class CreateKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    KnowledgeIndexingRequestDispatcher indexingDispatcher)
    : ICommandHandler<CreateKnowledgeDocumentCommand, CreateKnowledgeDocumentResult>
{
    public async ValueTask<CreateKnowledgeDocumentResult> Handle(CreateKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // Base inativa aceita documento normalmente: desativar impede o uso
        // pelo agente, não a manutenção do conteúdo.
        var contentMode = await dbContext.KnowledgeBases
            .Where(knowledgeBase => knowledgeBase.Id == command.KnowledgeBaseId)
            .Select(knowledgeBase => (KnowledgeBaseContentMode?)knowledgeBase.ContentMode)
            .FirstOrDefaultAsync(cancellationToken);

        if (contentMode is null)
        {
            return CreateKnowledgeDocumentResult.KnowledgeBaseNotFound();
        }

        // Em base sincronizada os documentos vêm da pasta, escritos pelo subject de
        // serviço (catalogo-base-sincronizada, D7). Recusado antes de processar o
        // conteúdo. Se esta recusa sumir, a FK composta recusa a linha no banco: o
        // construtor abaixo cria sempre documento de base manual (D4).
        if (contentMode == KnowledgeBaseContentMode.Synced)
        {
            return CreateKnowledgeDocumentResult.SyncedKnowledgeBase();
        }

        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return CreateKnowledgeDocumentResult.Invalid(content.Refusal!);
        }

        var document = new KnowledgeDocument(
            command.KnowledgeBaseId, command.Title, command.SourceType, content.ExtractedText!);

        // O evento entra no MESMO SaveChanges do documento (historico-documentos-base,
        // D2): os dois são gravados juntos ou nenhum. As recusas acima retornam
        // antes daqui, e por isso cadastro recusado não deixa evento.
        //
        // O pedido de indexação também (indexacao-sem-job-orfao, D1): documento e
        // pedido gravados juntos ou nenhum dos dois.
        var indexingRequest = KnowledgeIndexingRequest.For(document);
        dbContext.KnowledgeDocuments.Add(document);
        dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Created(document, command.Author));
        dbContext.KnowledgeIndexingRequests.Add(indexingRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Despacha DEPOIS do SaveChanges, nunca antes: uma mensagem publicada antes
        // da gravação apontaria para um documento que o consumidor não acharia. Não
        // lança: com a fila fora do ar o pedido fica para a varredura, e a resposta é
        // o 201 de sempre (D2).
        await indexingDispatcher.DispatchAfterWriteAsync([indexingRequest.Id], cancellationToken);

        // ContentLengthBytes é coluna gerada pelo banco: o EF a lê de volta no
        // próprio SaveChanges, sem Reload() explícito (design.md, D14).
        return CreateKnowledgeDocumentResult.Success(KnowledgeDocumentResponse.FromEntity(document));
    }
}
