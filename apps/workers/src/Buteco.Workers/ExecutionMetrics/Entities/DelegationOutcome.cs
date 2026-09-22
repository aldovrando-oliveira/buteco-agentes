namespace Buteco.Workers.ExecutionMetrics.Entities;

/// <summary>
/// Espelho de <c>Buteco.Api.ExecutionMetrics.Entities.DelegationOutcome</c> —
/// uma linha por delegação disparada (D5). É o gêmeo durável do log de
/// desistência de <c>AgentDelegationToolSetResolver</c>, e carrega todo campo
/// que o log carrega: sem isso, o log não pode sair (D11).
/// </summary>
public class DelegationOutcome
{
    public Guid Id { get; private set; }

    public string SourceTaskId { get; private set; } = null!;

    public Guid SourceAgentId { get; private set; }

    public Guid TargetAgentId { get; private set; }

    /// <summary>Nulo quando a task do alvo nunca foi criada (<c>NotStarted</c>).</summary>
    public string? TargetTaskId { get; private set; }

    public string Outcome { get; private set; } = null!;

    /// <summary>
    /// Último estado <b>lido</b> do alvo — não "o estado na desistência", pelo
    /// mesmo motivo do log (entre a última leitura e o cancelamento cabe um
    /// intervalo de polling inteiro). Ausência de leitura é nulo com
    /// <see cref="SuccessfulReadCount"/> zero, nunca um estado sentinela.
    /// </summary>
    public string? LastObservedTargetState { get; private set; }

    public DateTimeOffset? LastObservedAt { get; private set; }

    public int SuccessfulReadCount { get; private set; }

    public double DurationMs { get; private set; }

    private DelegationOutcome()
    {
    }

    public DelegationOutcome(
        string sourceTaskId,
        Guid sourceAgentId,
        Guid targetAgentId,
        string? targetTaskId,
        string outcome,
        string? lastObservedTargetState,
        DateTimeOffset? lastObservedAt,
        int successfulReadCount,
        double durationMs)
    {
        Id = Guid.NewGuid();
        SourceTaskId = sourceTaskId;
        SourceAgentId = sourceAgentId;
        TargetAgentId = targetAgentId;
        TargetTaskId = targetTaskId;
        Outcome = outcome;
        LastObservedTargetState = lastObservedTargetState;
        LastObservedAt = lastObservedAt;
        SuccessfulReadCount = successfulReadCount;
        DurationMs = durationMs;
    }
}
