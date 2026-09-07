using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;

public sealed class CreateKnowledgeBaseCommandHandler(AppDbContext dbContext)
    : ICommandHandler<CreateKnowledgeBaseCommand, KnowledgeBaseResponse>
{
    public async ValueTask<KnowledgeBaseResponse> Handle(CreateKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        var knowledgeBase = new KnowledgeBase(command.Name, command.Description);

        dbContext.KnowledgeBases.Add(knowledgeBase);
        await dbContext.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseResponse.FromEntity(knowledgeBase);
    }
}
