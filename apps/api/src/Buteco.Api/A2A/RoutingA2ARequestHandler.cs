using System.Runtime.CompilerServices;
using global::A2A;
using Microsoft.AspNetCore.Http;

namespace Buteco.Api.A2A;

/// <summary>
/// Ponto único mapeado no startup (<c>/agents/{id}/a2a</c>) que resolve, por
/// requisição, qual <see cref="A2AServer"/> do <see cref="AgentA2AServerRegistry"/>
/// deve tratar a chamada — o route value <c>id</c> não é conhecido em tempo de
/// registro de DI, só em tempo de requisição.
/// </summary>
public sealed class RoutingA2ARequestHandler(
    IHttpContextAccessor httpContextAccessor,
    AgentA2AServerRegistry registry) : IA2ARequestHandler
{
    private async Task<IA2ARequestHandler> ResolveAsync(CancellationToken cancellationToken)
    {
        var routeValues = httpContextAccessor.HttpContext?.Request.RouteValues;
        if (routeValues is null
            || !routeValues.TryGetValue("id", out var idValue)
            || idValue is not string idText
            || !Guid.TryParse(idText, out var agentId))
        {
            throw new A2AException("Rota de agente inválida.", A2AErrorCode.InvalidRequest);
        }

        var server = await registry.GetOrCreateAsync(agentId, cancellationToken);
        return server ?? throw new A2AException($"Agente '{agentId}' não encontrado.", A2AErrorCode.InvalidRequest);
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.SendMessageAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        await foreach (var item in handler.SendStreamingMessageAsync(request, cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.GetTaskAsync(request, cancellationToken);
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.ListTasksAsync(request, cancellationToken);
    }

    public async Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.CancelTaskAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        await foreach (var item in handler.SubscribeToTaskAsync(request, cancellationToken))
        {
            yield return item;
        }
    }

    public async Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.CreateTaskPushNotificationConfigAsync(request, cancellationToken);
    }

    public async Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.GetTaskPushNotificationConfigAsync(request, cancellationToken);
    }

    public async Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.ListTaskPushNotificationConfigAsync(request, cancellationToken);
    }

    public async Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        await handler.DeleteTaskPushNotificationConfigAsync(request, cancellationToken);
    }

    public async Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
    {
        var handler = await ResolveAsync(cancellationToken);
        return await handler.GetExtendedAgentCardAsync(request, cancellationToken);
    }
}
