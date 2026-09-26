using global::A2A;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Providers;
using Buteco.Api.RejectionMetrics;
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

        // AUSÊNCIA DE LINHA NÃO É INATIVIDADE (design.md da change
        // recusa-motivo-coleta, D4). Antes desta change a projeção ia para um
        // `record struct` e o FirstOrDefaultAsync devolvia `default` — com
        // IsActive = false — quando o agente não existia, então agente inexistente
        // caía no mesmo `if` de agente inativo. O comportamento do protocolo é o
        // mesmo (recusa, sem publicar job) e o MOTIVO gravado não: colapsá-los
        // afirmaria um estado que ninguém leu (convenção 13), e para quem opera
        // são problemas diferentes — um se resolve reativando, o outro é cliente
        // chamando o endereço A2A de um agente que não está lá.
        if (agentState is null)
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            await RecordRejectionAsync(context.TaskId, RejectionMetricsValues.Reason.AgentNotFound, cancellationToken);
            return;
        }

        if (!agentState.IsActive)
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            await RecordRejectionAsync(context.TaskId, RejectionMetricsValues.Reason.AgentInactive, cancellationToken);
            return;
        }

        // Agente cadastrado antes desta capacidade existir (Provider/Model
        // nulos) está no estado implícito "precisa de reconfiguração" — ver
        // design.md da change backend-multi-provedor-llm, Decision 6.
        if (agentState.Provider is null || agentState.Model is null)
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            await RecordRejectionAsync(
                context.TaskId, RejectionMetricsValues.Reason.ProviderOrModelMissing, cancellationToken);
            return;
        }

        // Provider estava configurado no cadastro/edição, mas pode ter
        // deixado de estar (chave removida do ambiente) — ver Decision 5.
        if (!providerCatalogService.IsProviderConfigured(agentState.Provider))
        {
            await updater.RejectAsync(cancellationToken: cancellationToken);
            await RecordRejectionAsync(
                context.TaskId, RejectionMetricsValues.Reason.ProviderNotConfigured, cancellationToken);
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

        var message = new TaskJobMessage(context.TaskId, agentId, context.ContextId, context.Configuration?.PushNotificationConfig);
        await taskJobPublisher.PublishAsync(message, cancellationToken);
    }

    /// <summary>
    /// A métrica M29, gravada SEMPRE depois do <c>RejectAsync</c> (design.md, D8):
    /// o escritor não lança, e a ordem garante que uma falha de gravação não possa
    /// alterar o estado em que a task terminou nem fazer o job ser publicado.
    /// </summary>
    private async Task RecordRejectionAsync(string taskId, string reason, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<RejectionMetricsWriter>();

        await writer.WriteAsync(taskId, agentId, reason, cancellationToken);
    }

    /// <summary>
    /// Devolve <c>null</c> quando não existe agente com este id — a distinção que
    /// o <c>record struct</c> apagava. Ver o comentário do primeiro caminho de
    /// recusa em <see cref="ExecuteAsync"/>.
    /// </summary>
    private async Task<AgentState?> GetAgentStateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var state = await dbContext.Agents
            .AsNoTracking()
            .Where(agent => agent.Id == agentId)
            .Select(agent => new AgentState(agent.IsActive, agent.Provider, agent.Model))
            .FirstOrDefaultAsync(cancellationToken);

        return state;
    }

    /// <summary>
    /// <b>Record de referência, e não <c>record struct</c></b>: é o tipo que faz o
    /// <c>FirstOrDefaultAsync</c> devolver <c>null</c> para "não há agente" em vez
    /// do <c>default</c> com <c>IsActive = false</c>, que é o que colapsava as duas
    /// causas. A distinção vive no TIPO, não numa checagem que alguém possa
    /// remover por engano.
    /// </summary>
    private sealed record AgentState(bool IsActive, string? Provider, string? Model);
}
