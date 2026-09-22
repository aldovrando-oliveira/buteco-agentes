namespace Buteco.Api.ExecutionMetrics.Entities;

/// <summary>
/// O resultado de uma delegação disparada pela tool de delegação dentro de uma
/// execução (design.md da change <c>metricas-execucao-coleta</c>, D5). Só
/// mapeamento: quem escreve é <c>apps/workers</c> (ver o comentário de
/// <see cref="TaskExecution"/>).
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
    /// Último estado <b>lido</b> do alvo, não o estado no instante da
    /// desistência. Nulo com <see cref="SuccessfulReadCount"/> zero é "não sei";
    /// nulo com contagem maior que zero é "a linha não estava lá".
    /// </summary>
    public string? LastObservedTargetState { get; private set; }

    public DateTimeOffset? LastObservedAt { get; private set; }

    public int SuccessfulReadCount { get; private set; }

    public double DurationMs { get; private set; }

    private DelegationOutcome()
    {
    }
}
