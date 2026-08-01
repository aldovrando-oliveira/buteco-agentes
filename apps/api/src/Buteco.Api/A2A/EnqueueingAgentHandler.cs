using global::A2A;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

public sealed class EnqueueingAgentHandler(
    Guid agentId,
    ITaskJobPublisher taskJobPublisher,
    IServiceScopeFactory scopeFactory,
    ProviderCatalogService providerCatalogService) : IAgentHandler
{
    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await updater.SubmitAsync(cancellationToken);

        // Estado do agente é lido do banco a cada execução (não do cache do
        // AgentA2AServerRegistry, que só verifica existência uma vez por
        // processo) para que uma mudança de estado depois do primeiro
        // SendMessage (desativação, edição, chave de provider removida) seja
        // recusada a partir da próxima chamada.
        var agentState = await GetAgentStateAsync(cancellationToken);

        if (!agentState.IsActive)
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            return;
        }

        // Agente cadastrado antes desta capacidade existir (Provider/Model
        // nulos) está no estado implícito "precisa de reconfiguração" — ver
        // design.md da change backend-multi-provedor-llm, Decision 6.
        if (agentState.Provider is null || agentState.Model is null)
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            return;
        }

        // Provider estava configurado no cadastro/edição, mas pode ter
        // deixado de estar (chave removida do ambiente) — ver Decision 5.
        if (!providerCatalogService.IsProviderConfigured(agentState.Provider))
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

    private async Task<AgentState> GetAgentStateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId)
            .Select(agent => new AgentState(agent.IsActive, agent.Provider, agent.Model))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private readonly record struct AgentState(bool IsActive, string? Provider, string? Model);
}
