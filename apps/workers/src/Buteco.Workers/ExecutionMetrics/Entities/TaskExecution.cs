namespace Buteco.Workers.ExecutionMetrics.Entities;

/// <summary>
/// Espelho de <c>Buteco.Api.ExecutionMetrics.Entities.TaskExecution</c> — uma
/// linha por execução de task consumida (design.md da change
/// <c>metricas-execucao-coleta</c>, D1). <c>apps/workers</c> escreve;
/// <c>apps/api</c> só migra.
///
/// <para>
/// <b>Nasce aberta e é fechada depois</b> (D3): o construtor corresponde ao
/// INSERT feito logo depois de a task ser lida, antes de qualquer caminho que a
/// termine; <see cref="Close"/> corresponde ao UPDATE feito depois de o estado
/// terminal estar gravado. Uma linha que ficou com <see cref="EndedAt"/> nulo é
/// uma execução que não chegou ao fechamento — é o dado de "execução sem estado
/// terminal", e ele não existiria se a linha só nascesse no fim.
/// </para>
///
/// <para>
/// <b>Sem chave estrangeira para <c>agents</c></b> (D13):
/// <see cref="Provider"/>/<see cref="Model"/> são snapshot do agente no início.
/// </para>
/// </summary>
public class TaskExecution
{
    public string TaskId { get; private set; } = null!;

    public Guid AgentId { get; private set; }

    public string ContextId { get; private set; } = null!;

    public string? Provider { get; private set; }

    public string? Model { get; private set; }

    public string Origin { get; private set; } = null!;

    public Guid? SourceAgentId { get; private set; }

    public string? SourceTaskId { get; private set; }

    public int DelegationDepth { get; private set; }

    /// <summary>
    /// Carimbo do <c>submitted</c> lido antes de ser sobrescrito; nulo quando a
    /// task lida não estava em <c>Submitted</c> (reentrega) — D4.
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? LockAcquiredAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public string? TerminalState { get; private set; }

    public string? FailurePhase { get; private set; }

    private TaskExecution()
    {
    }

    public TaskExecution(
        string taskId,
        Guid agentId,
        string contextId,
        string? provider,
        string? model,
        string origin,
        Guid? sourceAgentId,
        string? sourceTaskId,
        int delegationDepth,
        DateTimeOffset? submittedAt,
        DateTimeOffset startedAt)
    {
        TaskId = taskId;
        AgentId = agentId;
        ContextId = contextId;
        Provider = provider;
        Model = model;
        Origin = origin;
        SourceAgentId = sourceAgentId;
        SourceTaskId = sourceTaskId;
        DelegationDepth = delegationDepth;
        SubmittedAt = submittedAt;
        StartedAt = startedAt;
    }

    public void Close(
        DateTimeOffset? lockAcquiredAt, DateTimeOffset? endedAt, string? terminalState, string? failurePhase)
    {
        LockAcquiredAt = lockAcquiredAt;
        EndedAt = endedAt;
        TerminalState = terminalState;
        FailurePhase = failurePhase;
    }
}
