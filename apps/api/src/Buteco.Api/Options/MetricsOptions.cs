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
    /// <b>É um MAPA, e não dois campos, de propósito</b> (design.md, D8): hoje
    /// são dois regimes com datas diferentes — a coleta de execução e a de
    /// embedding —, e um "medindo desde" único mentiria sobre um dos dois. A
    /// etapa 4 da linha acrescenta um terceiro, e o mapa o absorve sem mudar o
    /// contrato.
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

    /// <summary>Regime da coleta de execução — M1, M2, M6, M7, M9–M17, M21–M29, M32, M34.</summary>
    public const string ExecutionRegime = "execution";

    /// <summary>Regime da coleta de embedding — M19 e M30.</summary>
    public const string EmbeddingRegime = "embedding";
}
