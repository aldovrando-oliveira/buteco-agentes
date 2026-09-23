using Microsoft.Extensions.AI;

namespace Buteco.Workers.ExecutionMetrics;

/// <summary>
/// Marca como <see cref="ExecutionMetricsValues.Purpose.Compaction"/> as
/// requisições que a <c>SummarizationCompactionStrategy</c> faz, e delega ao
/// client compartilhado (design.md da change <c>metricas-execucao-coleta</c>, D8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> <c>AgentExecutionService</c> passa à estratégia de
/// compactação o MESMO client do turno, e o client não tem como saber quem o
/// chamou. Sem a marca, a chamada de resumo — paga, e disparada a cada dez turnos
/// — entraria nas métricas como se fosse um turno do usuário.
/// </para>
///
/// <para>
/// <b>IMPLEMENTA <c>IChatClient</c> DIRETO, NÃO DERIVA DE
/// <c>DelegatingChatClient</c>, E <c>Dispose</c> NÃO FAZ NADA.</b>
/// <c>DelegatingChatClient.Dispose()</c> descarta o inner em cascata, e o inner
/// aqui é o client compartilhado do processo inteiro (comentário de
/// <c>ChatClientResolver.Build</c>): descartá-lo quebraria toda mensagem seguinte
/// daquele <c>(provider, model)</c>. Hoje nada descartaria este wrapper —
/// decompilada (<c>Microsoft.Agents.AI</c> 1.15.0), a estratégia guarda o client
/// e só chama <c>GetResponseAsync(list, null, ct)</c>, sem <c>Dispose</c> —, mas a
/// garantia não pode depender de ninguém escrever <c>using</c> nele no futuro.
/// Este tipo não é dono do inner.
/// </para>
///
/// <para>
/// <b>Recusado: distinguir pela ausência de opções.</b> A estratégia passa
/// <c>ChatOptions</c> nulo e o turno passa opções preenchidas, então
/// <c>options is null</c> separaria os dois hoje. É propriedade da versão
/// instalada do pacote, não contrato: a próxima versão pode passar opções, e a
/// finalidade gravada mudaria de valor em silêncio.
/// </para>
/// </remarks>
public sealed class CompactionCallChatClient(IChatClient innerClient) : IChatClient
{
    /// <summary>
    /// Mensagem de usuário acrescentada ao fim da requisição de resumo quando a
    /// última mensagem é de assistente.
    ///
    /// <para>
    /// <b>ESTE TEXTO É LIDO PELO MODELO — não é string de formatação, e trocá-lo
    /// muda o resumo.</b> Ele é deliberadamente uma <b>afirmação de fronteira</b>,
    /// e não um segundo comando de resumir: quem comanda é a instrução
    /// <c>system</c> que a <c>SummarizationCompactionStrategy</c> já manda
    /// (<i>"You are a conversation summarizer. Produce a concise summary…"</i>,
    /// com quatro critérios de preservação). Um "resuma a conversa acima" aqui
    /// competiria com ela — é mais recente e mais específico em posição, e
    /// passaria a valer como refinamento sem carregar nenhum dos quatro
    /// critérios, piorando o resumo exatamente no que o prompt do pacote existe
    /// para preservar. Em inglês pelo mesmo motivo: é o idioma do prompt que ele
    /// acompanha, e o idioma do resumo produzido segue o da conversa, não o da
    /// instrução (design.md da change <c>compactacao-historico</c>, D2).
    /// </para>
    /// </summary>
    private const string ConversationBoundaryMessage = "End of the conversation to summarize.";

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionMetricsScope.MarkPurpose(ExecutionMetricsValues.Purpose.Compaction);
        return await innerClient.GetResponseAsync(EnsureDoesNotEndWithModelTurn(messages), options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionMetricsScope.MarkPurpose(ExecutionMetricsValues.Purpose.Compaction);
        var enumerable = innerClient.GetStreamingResponseAsync(
            EnsureDoesNotEndWithModelTurn(messages), options, cancellationToken);

        await foreach (var update in enumerable)
        {
            yield return update;
        }
    }

    /// <summary>
    /// Devolve a requisição de resumo terminando em mensagem de usuário.
    ///
    /// <para>
    /// <b>O defeito que isto corrige.</b> O Gemini recusa com
    /// <c>400 — "Requests ending with a model turn are not supported."</c> uma
    /// requisição encerrada em turno de modelo, e a requisição de resumo termina
    /// assim <b>sempre, por construção do pacote</b>: o <c>TurnIndex</c> é
    /// copiado para os grupos de assistente e a contagem de turnos só cai quando
    /// o grupo de assistente do turno também é excluído, então o laço de exclusão
    /// para logo depois dele. Medido no piloto (seis chamadas, seis falhas) e
    /// reproduzido contra o Gemini real em 22/09/2026, 21:15,
    /// <c>America/Sao_Paulo</c>, <c>gemini-3.6-flash</c>, com
    /// <c>Google.GenAI</c> 1.15.0 — 256 e 281 ms até o <c>ClientError</c>.
    /// OpenAI e Anthropic aceitam a mesma requisição (prefill), então o defeito é
    /// do par (payload, provedor), não da compactação.
    /// </para>
    ///
    /// <para>
    /// <b>Condicional de propósito.</b> Acrescentar sempre seria mais curto de
    /// ler e poria uma mensagem a mais num prompt que já era válido. O custo da
    /// condição é comparar um papel.
    /// </para>
    ///
    /// <para>
    /// <b>Não altera o histórico.</b> Trocar o papel da última mensagem para
    /// usuário — a correção que parece mais barata — falsificaria a autoria
    /// dentro do prompt: o modelo leria a própria resposta como fala do usuário.
    /// Descartar o último grupo perderia conteúdo do resumo e deixaria buraco no
    /// histórico, porque a estratégia já o marcou como excluído.
    /// </para>
    /// </summary>
    private static IEnumerable<ChatMessage> EnsureDoesNotEndWithModelTurn(IEnumerable<ChatMessage> messages)
    {
        var list = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
        if (list.Count == 0 || list[^1].Role != ChatRole.Assistant)
        {
            return list;
        }

        return [.. list, new ChatMessage(ChatRole.User, ConversationBoundaryMessage)];
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        innerClient.GetService(serviceType, serviceKey);

    /// <summary>No-op de propósito: este tipo não é dono do client compartilhado.</summary>
    public void Dispose()
    {
    }
}
