using global::A2A;
using Buteco.Api.Messaging;

namespace Buteco.Api.A2A;

public sealed class EnqueueingAgentHandler(Guid agentId, ITaskJobPublisher taskJobPublisher) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken);

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
}
