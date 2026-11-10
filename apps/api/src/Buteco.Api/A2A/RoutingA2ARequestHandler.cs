using System.Runtime.CompilerServices;
using Buteco.Api.Infrastructure;
using global::A2A;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

/// <summary>
/// Ponto único mapeado no startup (<c>/agents/{id}/a2a</c>) que resolve, por
/// requisição, qual <see cref="A2AServer"/> do <see cref="AgentA2AServerRegistry"/>
/// deve tratar a chamada — o route value <c>id</c> não é conhecido em tempo de
/// registro de DI, só em tempo de requisição.
///
/// <para>
/// <b>A aplicação externa só alcança as próprias tasks no agente da rota</b> (design.md da change
/// agentes-por-aplicacao, D3; spec <c>a2a-task-lifecycle</c>). Quando quem chama é o subject
/// <c>application</c> (o <see cref="ApplicationA2AFilter"/> já conferiu o vínculo e a operação), este
/// handler:
/// <list type="bullet">
/// <item>põe a aplicação como dona da task que o <c>SendMessage</c> cria (<see cref="TaskOwnerScope"/>);</item>
/// <item>no <c>GetTask</c> e no <c>SendMessage</c> com <c>message.taskId</c>, exige a task com o dono e o
/// agente da rota, senão responde o mesmo erro do SDK para task inexistente. O agente entra porque o
/// <see cref="PostgresTaskStore"/> lê a task só pelo id: sem ele, a rota de um agente leria a task de
/// outro;</item>
/// <item>no <c>SendMessage</c> com <c>contextId</c>, exige uma task dela, neste agente, com esse contexto.
/// O SDK aceita um <c>contextId</c> desconhecido como conversa nova, e o worker monta o histórico com as
/// tasks do agente naquele contexto; por isso o inexistente e o alheio recebem a mesma resposta (Q5,
/// decisão do dono), e a conversa nova se abre sem <c>contextId</c>.</item>
/// </list>
/// O <c>service:inbox</c> não passa por nada disso.
/// </para>
/// </summary>
public sealed class RoutingA2ARequestHandler(
    IHttpContextAccessor httpContextAccessor,
    AgentA2AServerRegistry registry,
    IServiceScopeFactory scopeFactory) : IA2ARequestHandler
{
    public const string ContextNotFoundMessage = "Contexto não encontrado.";

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
        var application = ApplicationOf();
        if (application is { } applicationId)
        {
            if (!string.IsNullOrEmpty(request.Message?.TaskId))
            {
                await EnsureOwnTaskAsync(applicationId, request.Message.TaskId, cancellationToken);
            }

            if (!string.IsNullOrEmpty(request.Message?.ContextId))
            {
                await EnsureOwnContextAsync(applicationId, request.Message.ContextId, cancellationToken);
            }
        }

        using (TaskOwnerScope.Begin(application))
        {
            return await handler.SendMessageAsync(request, cancellationToken);
        }
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
        if (ApplicationOf() is { } applicationId)
        {
            await EnsureOwnTaskAsync(applicationId, request.Id, cancellationToken);
        }

        return await handler.GetTaskAsync(request, cancellationToken);
    }

    /// <summary>
    /// A aplicação que chama, ou <c>null</c> para outro subject. Um subject <c>application</c> sem id legível
    /// não chega aqui: o <see cref="ApplicationA2AFilter"/> o recusa com <c>404</c>.
    /// </summary>
    private Guid? ApplicationOf() => ApplicationCaller.IdOf(httpContextAccessor.HttpContext?.User);

    private Guid RouteAgentId() =>
        httpContextAccessor.HttpContext?.Request.RouteValues.TryGetValue("id", out var idValue) == true &&
        idValue is string idText && Guid.TryParse(idText, out var agentId)
            ? agentId
            : throw new A2AException("Rota de agente inválida.", A2AErrorCode.InvalidRequest);

    /// <summary>
    /// A task com este dono e o agente da rota; senão a mesma exceção que o <c>A2AServer</c> lança para task
    /// inexistente (mesma mensagem, mesmo código), para a resposta não dizer se a task existe.
    /// </summary>
    private async Task EnsureOwnTaskAsync(Guid applicationId, string taskId, CancellationToken cancellationToken)
    {
        var agentId = RouteAgentId();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var own = await dbContext.A2ATasks.AsNoTracking().AnyAsync(
            task => task.TaskId == taskId && task.AgentId == agentId && task.ApplicationId == applicationId,
            cancellationToken);
        if (!own)
        {
            throw new A2AException($"Task '{taskId}' not found.", A2AErrorCode.TaskNotFound);
        }
    }

    private async Task EnsureOwnContextAsync(Guid applicationId, string contextId, CancellationToken cancellationToken)
    {
        var agentId = RouteAgentId();
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var own = await dbContext.A2ATasks.AsNoTracking().AnyAsync(
            task => task.ContextId == contextId && task.AgentId == agentId && task.ApplicationId == applicationId,
            cancellationToken);
        if (!own)
        {
            throw new A2AException(ContextNotFoundMessage, A2AErrorCode.InvalidParams);
        }
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
