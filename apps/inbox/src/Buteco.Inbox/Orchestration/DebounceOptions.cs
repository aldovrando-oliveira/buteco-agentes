namespace Buteco.Inbox.Orchestration;

public sealed class DebounceOptions
{
    public const string SectionName = "Debounce";

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int MaxDispatchAttempts { get; set; } = 3;

    /// <summary>
    /// Teto da serialização por sessão (change inbox-serializacao-por-sessao, D3): uma
    /// linha em <c>Dispatching</c> segura a <c>Pending</c> da mesma sessão até o
    /// <c>LastMessageAt</c> dela ficar mais velho que este valor. Passado o teto, a
    /// linha deixa de bloquear, sem ser alterada: continua sendo da reconciliação.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por que existe:</b> uma task que nunca chega a estado terminal (agente ou task
    /// não encontrados no worker, exceção que escapa de <c>ExecuteAsync</c>) deixaria a
    /// sessão bloqueada para sempre. Sem o teto, o defeito deixa de custar uma mensagem
    /// e passa a custar a conversa.
    /// </para>
    /// <para>
    /// <b>A derivação de 10 min:</b> cobre <b>uma</b> resposta travada do provedor (até
    /// 400 s, #134) mais <b>uma</b> delegação (até 120 s), 8 min 40 s, com folga. É o
    /// mesmo valor de <see cref="DispatchReconciliationOptions.UntrackedDispatchMaxAge"/>:
    /// a linha sem <c>TaskId</c> deixa de bloquear praticamente quando a reconciliação a
    /// encerra, e o operador lê um número só. Conta de <c>LastMessageAt</c>, que é
    /// anterior ao claim em pelo menos <see cref="Window"/>, então o teto real fica entre
    /// 9 min 50 s e 10 min.
    /// </para>
    /// <para>
    /// <b>O que passa dele, e por que é aceitável:</b> turnos com várias chamadas
    /// travadas ou várias delegações, e o tempo de fila no RabbitMQ (que consome o mesmo
    /// orçamento). Passar do teto devolve o comportamento anterior à change (a
    /// <c>Pending</c> sai e o worker aplica os 30 s do lock), nunca algo pior.
    /// </para>
    /// <para>
    /// <b>Não foi medido</b> como duração real de turno: a medição que existe é a do
    /// pior caso de rede (#134). <b>Gatilho de recalibração</b> (convenção 22): a
    /// <c>task_executions</c> mostrar turnos acima de 5 min, ou a #134 mudar o teto de
    /// resposta travada, ou <c>UntrackedDispatchMaxAge</c> mudar.
    /// </para>
    /// <para>
    /// Configurável pelo mesmo motivo de <see cref="Window"/>: os testes precisam de
    /// valores curtos. Validado no boot por
    /// <see cref="DebounceOptionsValidation.ValidateDebounceSessionSerializationMaxWait"/>.
    /// </para>
    /// </remarks>
    public TimeSpan SessionSerializationMaxWait { get; set; } = TimeSpan.FromMinutes(10);
}
