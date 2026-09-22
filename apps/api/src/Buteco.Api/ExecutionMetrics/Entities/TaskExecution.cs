namespace Buteco.Api.ExecutionMetrics.Entities;

/// <summary>
/// Uma execução de task consumida por <c>apps/workers</c> — a linha pai da
/// coleta de métricas de execução (design.md da change
/// <c>metricas-execucao-coleta</c>, D1).
///
/// <para>
/// <b>Esta entidade vive em <c>apps/api</c>, que nunca escreve nela.</b> Quem
/// escreve é <c>apps/workers</c>; a tabela nasce aqui porque o <c>migrator</c>
/// do <c>docker-compose.prod.yml</c> só empacota bundles de <c>apps/api</c> e
/// <c>apps/inbox</c> — o mesmo motivo de <c>KnowledgeFragment</c>. As três
/// entidades desta pasta existem só para o modelo gerar a migração.
/// </para>
///
/// <para>
/// <b>Sem chave estrangeira para <c>agents</c>, de propósito</b> (D13):
/// <see cref="Provider"/> e <see cref="Model"/> são snapshot do agente no início
/// da execução. O consumo de ontem pertence ao modelo de ontem, e uma FK
/// convidaria a consulta a fazer join e ler o modelo de hoje.
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
    /// Nulo quando a task não estava em <c>Submitted</c> ao ser lida pelo worker
    /// (reentrega) — nunca o carimbo de outro estado (D4).
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? LockAcquiredAt { get; private set; }

    /// <summary>Nulo = execução aberta, sem estado terminal gravado por ela.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    public string? TerminalState { get; private set; }

    public string? FailurePhase { get; private set; }

    private TaskExecution()
    {
    }
}
