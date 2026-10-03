using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Commands.UpdateKnowledgeBase;

public sealed class UpdateKnowledgeBaseCommandHandler(AppDbContext dbContext)
    : ICommandHandler<UpdateKnowledgeBaseCommand, KnowledgeBaseResponse?>
{
    // Nome e descrição continuam editáveis em base sincronizada
    // (catalogo-base-sincronizada, D7). Tipo, provedor e pasta não estão no comando,
    // e o request não tem os campos: um PUT com eles não altera nada (D11).
    public async ValueTask<KnowledgeBaseResponse?> Handle(UpdateKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        var knowledgeBase = await dbContext.KnowledgeBases
            .FirstOrDefaultAsync(knowledgeBase => knowledgeBase.Id == command.Id, cancellationToken);

        if (knowledgeBase is null)
        {
            return null;
        }

        knowledgeBase.UpdateDetails(command.Name, command.Description);
        await dbContext.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseResponse.FromEntity(knowledgeBase);
    }
}
