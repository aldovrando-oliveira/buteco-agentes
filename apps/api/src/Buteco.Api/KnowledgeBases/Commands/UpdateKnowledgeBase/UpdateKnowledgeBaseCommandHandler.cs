using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Commands.UpdateKnowledgeBase;

public sealed class UpdateKnowledgeBaseCommandHandler(AppDbContext dbContext)
    : ICommandHandler<UpdateKnowledgeBaseCommand, KnowledgeBaseResponse?>
{
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
