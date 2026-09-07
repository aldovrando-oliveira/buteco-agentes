using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed class UpdateKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor)
    : ICommandHandler<UpdateKnowledgeDocumentCommand, UpdateKnowledgeDocumentResult>
{
    public async ValueTask<UpdateKnowledgeDocumentResult> Handle(UpdateKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // Filtra por KnowledgeBaseId E Id, nunca só por Id: sem isso seria
        // possível editar documento de outra base pelo caminho errado.
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(
                document => document.Id == command.Id && document.KnowledgeBaseId == command.KnowledgeBaseId,
                cancellationToken);

        if (document is null)
        {
            return UpdateKnowledgeDocumentResult.NotFound();
        }

        // Validação antes de tocar a entidade: conteúdo inválido ou acima do
        // teto deixa documento, revisão e estado exatamente como estavam.
        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return UpdateKnowledgeDocumentResult.Invalid(content.ValidationErrors!);
        }

        document.Update(command.Title, command.SourceType, content.ExtractedText!);
        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateKnowledgeDocumentResult.Success(KnowledgeDocumentResponse.FromEntity(document));
    }
}
