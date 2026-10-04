namespace Buteco.Inbox.Orchestration;

/// <summary>
/// Valores da reconciliação de PendingDispatch em Dispatching (design.md da change
/// pending-dispatch-orfa, D8). Configuráveis pelo mesmo motivo de
/// <see cref="DebounceOptions"/>: os testes precisam de valores curtos. Os padrões
/// são os de produção, cada um derivado de um número que existe em outro lugar, e
/// cada um com o gatilho de recalibração ao lado.
/// </summary>
public sealed class DispatchReconciliationOptions
{
    public const string SectionName = "DispatchReconciliation";

    /// <summary>
    /// Cadência da varredura. Um GetTask em apps/api por linha em Dispatching por
    /// ciclo; com a carência, a recuperação de uma órfã leva no pior caso ~3 min
    /// depois do estado terminal (D8).
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Tempo, contado do carimbo terminal da task, em que um push para a mesma
    /// task ainda pode estar sendo processado: 5 s do webhook em apps/workers
    /// (Program.cs, <c>PushNotificationSender.HttpClientName</c>) + 100 s do
    /// HttpClient anônimo dos senders de canal (timeout padrão) + gravação → 2 min
    /// (D3). Recalibrar quando o timeout do push ou dos senders mudar, ou quando um
    /// sender ganhar timeout próprio.
    /// </summary>
    public TimeSpan TerminalGrace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Idade da última mensagem além da qual uma linha em Dispatching SEM TaskId é
    /// encerrada como perda (D7). A reivindicação acontece depois de
    /// <c>Window</c> + até um <c>SweepInterval</c>; o SendMessage tem 5 s de
    /// timeout; e os candidatos de um ciclo são processados em série, até 5 s
    /// cada — 10 min cobre mais de 100 candidatos à frente no mesmo ciclo.
    /// Recalibrar quando <c>Debounce:Window</c>, <c>Debounce:SweepInterval</c> ou
    /// o timeout do cliente A2A mudarem, ou quando o disparo de um ciclo passar a
    /// ser concorrente.
    /// </summary>
    public TimeSpan UntrackedDispatchMaxAge { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Prazo de posse de uma reivindicação (D4): enquanto ela for mais nova que
    /// isto, nenhuma outra instância reivindica a mesma linha. Mesma derivação da
    /// carência — a entrega ao canal pode levar os 100 s do HttpClient dos senders,
    /// mais a gravação → 2 min. Vencido o prazo com a linha ainda em Dispatching, a
    /// instância que a reivindicou morreu ou foi parada no meio da entrega, e a
    /// linha volta a ser reivindicável (resíduo 2: entrega pelo menos uma vez).
    /// Recalibrar junto com <see cref="TerminalGrace"/>.
    /// </summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(2);
}
