using System.Collections.Concurrent;
using A2A;
using Buteco.Inbox.Orchestration;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// Substitui <see cref="IA2AClientFactory"/> nos testes de
/// <c>DebounceSweepService</c>/<c>InboundMessageOrchestrator</c> — sem rede
/// real, mesmo mecanismo de <see cref="FakeAgentApiHttpMessageHandler"/>,
/// mas no nível do cliente A2A (design.md, Decisão 3) em vez de
/// HttpMessageHandler. <see cref="Handler"/> é reatribuível por teste;
/// <see cref="RequestedAgentIds"/>/<see cref="Requests"/> usam coleções
/// thread-safe porque o teste de idempotência (6.7) chama esta fábrica a
/// partir de duas instâncias de <c>DebounceSweepService</c> concorrentes.
/// </summary>
public sealed class FakeA2AClientFactory : IA2AClientFactory
{
    public ConcurrentBag<Guid> RequestedAgentIds { get; } = [];

    public ConcurrentBag<SendMessageRequest> Requests { get; } = [];

    public Func<SendMessageRequest, SendMessageResponse> Handler { get; set; } = DefaultHandler;

    public IA2AClient CreateForAgent(Guid agentId)
    {
        RequestedAgentIds.Add(agentId);
        return new FakeA2AClient(this);
    }

    public static SendMessageResponse DefaultHandler(SendMessageRequest request) => new()
    {
        Task = new AgentTask
        {
            Id = Guid.NewGuid().ToString("N"),
            ContextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N"),
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow },
        },
    };

    public static SendMessageResponse RejectedHandler(SendMessageRequest request) => new()
    {
        Task = new AgentTask
        {
            Id = Guid.NewGuid().ToString("N"),
            ContextId = request.Message.ContextId ?? Guid.NewGuid().ToString("N"),
            Status = new TaskStatus { State = TaskState.Rejected, Timestamp = DateTimeOffset.UtcNow },
        },
    };

    private sealed class FakeA2AClient(FakeA2AClientFactory owner) : IA2AClient
    {
        public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        {
            owner.Requests.Add(request);
            return Task.FromResult(owner.Handler(request));
        }

        public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
