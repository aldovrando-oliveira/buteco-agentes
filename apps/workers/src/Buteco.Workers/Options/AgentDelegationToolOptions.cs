namespace Buteco.Workers.Options;

/// <summary>
/// Intervalo de poll e timeout de espera pela task delegada (design.md da
/// change apps-workers-delegacao-execucao, Decision 8). Não é vinculado a
/// nenhuma seção de configuração em <c>Program.cs</c> — os defaults abaixo
/// são, na prática, constantes de produto (mesmo espírito de
/// <c>MaxHistoryMessages</c>/<c>SummarizationTurnThreshold</c>); existir
/// como <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> em vez
/// de <c>const</c> serve só para permitir que os testes de concorrência
/// (ver <c>AgentDelegationConcurrencyTests</c>) sobrescrevam um timeout
/// curto sem depender de um deadlock real de 120s.
/// </summary>
public sealed class AgentDelegationToolOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);
}
