using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Commands.ActivateKnowledgeBase;

/// <summary>
/// Idempotente: activater uma base que já está nesse estado responde normalmente,
/// sem erro — mesmo comportamento de <c>McpServer</c>.
/// </summary>
public sealed class ActivateKnowledgeBaseCommandHandler(AppDbContext dbContext)
    : ICommandHandler<ActivateKnowledgeBaseCommand, KnowledgeBaseResponse?>
{
    public async ValueTask<KnowledgeBaseResponse?> Handle(ActivateKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        var knowledgeBase = await dbContext.KnowledgeBases
            .FirstOrDefaultAsync(knowledgeBase => knowledgeBase.Id == command.Id, cancellationToken);

        if (knowledgeBase is null)
        {
            return null;
        }

        knowledgeBase.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseResponse.FromEntity(knowledgeBase);
    }
}
