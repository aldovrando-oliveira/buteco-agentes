namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Como terminou uma execução de indexação. Existe como tipo, e não como
/// <c>bool</c> ou exceção, porque o <c>catch</c> do consumidor tem <b>duas</b>
/// saídas — republicar na fila de espera, ou terminar em <c>Failed</c> — e é
/// fácil uma delas escapar do tratamento.
///
/// <para>
/// O molde de <c>TaskJobConsumer</c> não cobre isso: lá o <c>catch</c> é o fim
/// do caminho (loga e descarta). Aqui ele <b>decide</b>, e a decisão precisa ser
/// um valor que o consumidor é obrigado a ramificar, não um efeito colateral
/// enterrado dentro do serviço.
/// </para>
/// </summary>
public enum KnowledgeIndexingOutcome
{
    /// <summary>Fragmentos gravados, documento em <c>Indexed</c>.</summary>
    Indexed,

    /// <summary>
    /// A revisão mudou ou o documento sumiu durante o trabalho. Nada foi
    /// gravado, e nada deve ser reenfileirado: o trabalho novo já está na fila
    /// (design.md, D5). **Não** é falha, e por isso não conta tentativa.
    /// </summary>
    Discarded,

    /// <summary>
    /// Falhou e ainda há tentativa disponível. O consumidor republica na fila de
    /// espera correspondente. A tentativa **já foi contada** e o documento
    /// continua em <c>Indexing</c>.
    /// </summary>
    RetryScheduled,

    /// <summary>
    /// Falhou e esgotou o limite. <b>É a única saída que grava
    /// <c>Failed</c></b>, e ela preserva <c>IndexedAt</c> e os fragmentos
    /// anteriores — garantia 3 de D9 da etapa 1.
    /// </summary>
    Failed,
}
