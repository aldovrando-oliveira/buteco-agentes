namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// A janela de D8 (design.md da change indexacao-sem-job-orfao): depois de uma
/// falha de despacho, as escritas pulam a tentativa no fim da requisição por
/// <see cref="KnowledgeIndexingRequestSchedule.SkipWindow"/>, e o pedido fica para a
/// varredura. Sem ela, o ciclo da #105 esperaria o limite inteiro em cada upsert com
/// o broker inacessível: 200 arquivos, 1.000 s por ciclo (R9).
///
/// <para>
/// <b>Em memória do processo, por instância</b>, de propósito: cada instância
/// descobre sozinha, na primeira falha, que o broker caiu, e paga isso uma vez. Um
/// processo novo começa com a janela fechada.
/// </para>
///
/// <para>
/// <b>Quem abre:</b> falha do despacho na requisição (erro ou limite) e falha de
/// <b>publicação</b> na varredura. A falha da <b>consulta</b> da varredura não abre:
/// não diz nada sobre o broker. <b>Quem fecha:</b> qualquer despacho que publicou.
/// </para>
/// </summary>
public sealed class KnowledgeIndexingDispatchWindow(TimeProvider timeProvider, KnowledgeIndexingRequestSchedule schedule)
{
    // Instante (ticks UTC) até o qual o despacho na requisição é pulado. Zero é
    // janela fechada. Lido e escrito por requisições concorrentes e pela varredura.
    private long _skipUntilTicks;

    /// <summary>Quantas vezes uma escrita pulou o despacho na requisição. Diagnóstico e teste.</summary>
    private long _skipped;

    public bool IsOpen => timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _skipUntilTicks);

    public long Skipped => Interlocked.Read(ref _skipped);

    public void Open() =>
        Interlocked.Exchange(ref _skipUntilTicks, (timeProvider.GetUtcNow() + schedule.SkipWindow).UtcTicks);

    public void Close() => Interlocked.Exchange(ref _skipUntilTicks, 0);

    internal void CountSkip() => Interlocked.Increment(ref _skipped);
}
