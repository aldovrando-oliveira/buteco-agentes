namespace Buteco.Api.Options;

/// <summary>
/// Configuração da agregação de métricas: o fuso do balde diário e os instantes
/// em que cada regime de medição começou.
/// </summary>
public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    /// <summary>
    /// Nome IANA canônico do fuso usado para agrupar por dia local.
    /// <para>
    /// <b>Vem de <c>TZ</c>, e é lido como CONFIGURAÇÃO</b> — nunca de
    /// <c>TimeZoneInfo.Local</c> nem de <c>CultureInfo.CurrentCulture</c>
    /// (design.md, D1). O mesmo valor é entregue a <c>apps/api</c> e a
    /// <c>apps/workers</c> com <c>:?</c> no compose, o que é o que faz os dois
    /// processos concordarem <b>por construção</b>: sem isso, nada impediria o
    /// balde de sair em UTC enquanto o worker renderiza em
    /// <c>America/Sao_Paulo</c>, e a série inteira mudaria de barra em silêncio.
    /// </para>
    /// <para>
    /// A decisão contraria a LETRA de uma decisão registrada ("nunca do <c>TZ</c>
    /// do processo de <c>apps/api</c>") e preserva a RAZÃO dela — o <c>:?</c> dá
    /// o mesmo valor a toda réplica, então o balde continua não dependendo de
    /// qual container respondeu (convenção 9).
    /// </para>
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Instante em que cada regime de medição começou, por nome de regime.
    /// <para>
    /// <b>É um MAPA, e não um campo por regime, de propósito</b> (design.md, D8):
    /// são TRÊS regimes com datas diferentes — a coleta de execução, a de
    /// embedding e a do motivo da recusa —, e um "medindo desde" único mentiria
    /// sobre dois deles.
    /// </para>
    ///
    /// <para>
    /// <b>E o mapa absorveu o terceiro sem mudar de forma</b>, que é para o que ele
    /// foi feito: a <c>recusa-motivo-coleta</c> acrescentou
    /// <see cref="RejectionRegime"/> sem tocar no contrato do campo. A previsão
    /// estava escrita aqui e na spec; ficou cumprida.
    /// </para>
    ///
    /// <para>
    /// <b>Regime declarado por um grupo de métricas e AUSENTE deste mapa reprova o
    /// boot</b> (<c>MetricsRegimeStartupValidation</c>, convenção 8). Sem essa
    /// checagem, a ausência não falha: ela DESLIGA o recorte daquele grupo, a
    /// janela pedida passa a valer inteira, e o período anterior à coleta vira
    /// contagem <c>0</c> em vez de ausência — número plausível, em silêncio.
    /// </para>
    /// <para>
    /// <b>A fonte é o instante do DEPLOY, não <c>min(StartedAt)</c>.</b> O menor
    /// carimbo é "quando a primeira linha chegou": se o sistema ficou ocioso
    /// depois do deploy, derivá-lo marcaria como NÃO MEDIDO um período que foi
    /// medido e estava vazio — exatamente a distinção que a rota existe para
    /// preservar (convenção 13). O <c>min</c> continua valendo como conferência.
    /// </para>
    /// </summary>
    public Dictionary<string, DateTimeOffset> Regimes { get; init; } = [];

    /// <summary>Regime da coleta de execução — M1, M2, M6, M7, M9–M17, M21–M28, M32, M34.</summary>
    public const string ExecutionRegime = "execution";

    /// <summary>Regime da coleta de embedding — M19 e M30.</summary>
    public const string EmbeddingRegime = "embedding";

    /// <summary>
    /// Regime da coleta do motivo da recusa — M29, e a contagem de recusas de
    /// entrada que a acompanha. Nasce com a <c>recusa-motivo-coleta</c>: antes
    /// dela o motivo não tinha fonte nenhuma, então não há como recortar por um
    /// regime que não existia.
    /// </summary>
    public const string RejectionRegime = "rejection";

    /// <summary>
    /// Os regimes que as rotas de agregação declaram, e portanto os que precisam
    /// de instante em <see cref="Regimes"/>. É esta lista que a checagem de boot
    /// percorre — quem acrescentar um regime acrescenta aqui, e o boot passa a
    /// exigir a configuração dele.
    /// </summary>
    public static readonly IReadOnlyList<string> DeclaredRegimes =
        [ExecutionRegime, EmbeddingRegime, RejectionRegime];
}
