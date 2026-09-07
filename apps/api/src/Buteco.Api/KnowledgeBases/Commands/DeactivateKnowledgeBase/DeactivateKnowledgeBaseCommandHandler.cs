using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeBases.Commands.DeactivateKnowledgeBase;

/// <summary>
/// Idempotente: deactivater uma base que já está nesse estado responde normalmente,
/// sem erro — mesmo comportamento de <c>McpServer</c>.
/// </summary>
public sealed class DeactivateKnowledgeBaseCommandHandler(AppDbContext dbContext)
    : ICommandHandler<DeactivateKnowledgeBaseCommand, KnowledgeBaseResponse?>
{
    public async ValueTask<KnowledgeBaseResponse?> Handle(DeactivateKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        var knowledgeBase = await dbContext.KnowledgeBases
            .FirstOrDefaultAsync(knowledgeBase => knowledgeBase.Id == command.Id, cancellationToken);

        if (knowledgeBase is null)
        {
            return null;
        }

        knowledgeBase.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseResponse.FromEntity(knowledgeBase);
    }
}
