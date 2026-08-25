using System.Globalization;

namespace Buteco.Workers.Agents;

/// <summary>
/// Monta o bloco de contexto temporal concatenado às <c>Instructions</c> do
/// agente a cada execução de task — nunca persistido, nunca parte do
/// histórico de conversa (design.md da change apps-workers-contexto-temporal,
/// Decisão 1). Função pura: recebe o <see cref="TimeProvider"/> já injetado
/// e o instante da mensagem (opcional — sempre ausente nesta etapa, nenhum
/// chamador de <c>apps/workers</c> tem outro valor para passar; o espaço fica
/// preparado para a etapa 2). Fuso e idioma nunca são herdados
/// implicitamente de <see cref="TimeZoneInfo.Local"/> ou de
/// <see cref="CultureInfo.CurrentCulture"/> — sempre via <paramref
/// name="timeProvider"/> e pt-BR fixo (design.md, Non-Goals).
/// </summary>
public static class TemporalContextBlockBuilder
{
    // Limiar de defasagem entre o instante de processamento e o instante da
    // mensagem acima do qual a linha de defasagem aparece no bloco —
    // constante global, não configurável por agente, mesmo padrão de
    // AgentExecutionService.MaxHistoryMessages/SummarizationTurnThreshold/
    // DelegationDepthLimit (design.md, Decisão 5; convenção 2). Em operação
    // normal a defasagem é de segundos; um agente que se desculpa por
    // atraso insignificante é bug de produto. Nesta etapa a linha nunca
    // aparece em produção (messageInstant é sempre null), mas o cálculo e o
    // teste do par acima/abaixo do limiar existem mesmo assim — a etapa 2
    // aciona isto sem precisar mudar esta função. Valor de partida (Open
    // Questions do design.md), ajustável sem redesenho.
    private static readonly TimeSpan StaleResponseThreshold = TimeSpan.FromMinutes(5);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private const string NotAUserMessageMarker =
        "[Contexto temporal — não é uma mensagem do usuário, não responda a ele diretamente]";

    public static string Build(TimeProvider timeProvider, DateTimeOffset? messageInstant = null)
    {
        var processingInstant = timeProvider.GetLocalNow();

        var lines = new List<string> { NotAUserMessageMarker };

        if (messageInstant is null)
        {
            lines.Add($"Instante atual (relógio do sistema): {FormatInstant(processingInstant)}");
            lines.Add(string.Empty);
            lines.Add(
                "Regra: toda expressão de tempo relativa no pedido do usuário " +
                "(\"amanhã\", \"sexta que vem\", \"daqui a uma hora\") se resolve " +
                "contra o instante acima.");

            return string.Join('\n', lines);
        }

        lines.Add($"Instante de processamento (agora, relógio do sistema): {FormatInstant(processingInstant)}");
        lines.Add($"Instante da mensagem (quando o usuário enviou): {FormatInstant(messageInstant.Value)}");

        var gap = processingInstant - messageInstant.Value;
        if (gap >= StaleResponseThreshold)
        {
            lines.Add(
                $"[Defasagem entre os dois: {FormatHumanDuration(gap)} — informe o usuário que a " +
                "resposta está atrasada, se isso for relevante para o pedido.]");
        }

        lines.Add(string.Empty);
        lines.Add(
            "Regra: toda expressão de tempo relativa no pedido do usuário " +
            "(\"amanhã\", \"sexta que vem\", \"daqui a uma hora\") se resolve contra o " +
            "instante da mensagem acima, não contra o instante de processamento. O " +
            "instante de processamento só serve para você avaliar se sua resposta " +
            "está chegando atrasada.");

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Concatena o bloco de contexto temporal às <c>Instructions</c> do
    /// operador — operador primeiro, bloco depois, separado por linha em
    /// branco dupla (design.md, Decisão 2). <paramref name="agentInstructions"/>
    /// vazia/em branco (Non-Goal: sem validação de não-vazio em
    /// <c>apps/api</c>) não pode deixar separador órfão no início do texto
    /// — usa só o bloco nesse caso. Separado de <see cref="Build"/>: quem
    /// decide a ordem e trata o caso degenerado é o ponto de montagem
    /// (<c>AgentExecutionService</c>), não o construtor do bloco em si.
    /// </summary>
    public static string Concatenate(string? agentInstructions, string temporalContextBlock) =>
        string.IsNullOrWhiteSpace(agentInstructions)
            ? temporalContextBlock
            : $"{agentInstructions}\n\n{temporalContextBlock}";

    private static string FormatInstant(DateTimeOffset instant) =>
        $"{instant.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)} ({instant.ToString("dddd", PtBr)})";

    private static string FormatHumanDuration(TimeSpan duration)
    {
        var parts = new List<string>();

        if (duration.Days > 0)
        {
            parts.Add(duration.Days == 1 ? "1 dia" : $"{duration.Days} dias");
        }

        if (duration.Hours > 0)
        {
            parts.Add(duration.Hours == 1 ? "1 hora" : $"{duration.Hours} horas");
        }

        if (duration.Minutes > 0 || parts.Count == 0)
        {
            parts.Add(duration.Minutes == 1 ? "1 minuto" : $"{duration.Minutes} minutos");
        }

        return parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts[..^1]) + " e " + parts[^1];
    }
}
