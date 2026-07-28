using global::A2A;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

public sealed class EnqueueingAgentHandler(
    Guid agentId,
    ITaskJobPublisher taskJobPublisher,
    IServiceScopeFactory scopeFactory) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken);

        // IsActive é lido do banco a cada execução (não do cache do
        // AgentA2AServerRegistry, que só verifica existência uma vez por
        // processo) para que um agente desativado depois do primeiro
        // SendMessage seja recusado a partir da próxima chamada.
        if (!await IsAgentActiveAsync(cancellationToken))
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            return;
        }

        // A2AServer só persiste a mensagem original em Task.History quando é uma
        // continuação (RequestContext.IsContinuation + AutoAppendHistory). Para uma
        // task nova isso não acontece sozinho, e o Worker (outro processo) só enxerga
        // o que estiver em Task.History — sem este passo, a mensagem do usuário nunca
        // chegaria ao Worker.
        if (context.Message is not null)
        {
            await eventQueue.EnqueueMessageAsync(context.Message, cancellationToken);
        }

        var message = new TaskJobMessage(context.TaskId, agentId, context.ContextId);
        await taskJobPublisher.PublishAsync(message, cancellationToken);
    }

    private async Task<bool> IsAgentActiveAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId)
            .Select(agent => agent.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
