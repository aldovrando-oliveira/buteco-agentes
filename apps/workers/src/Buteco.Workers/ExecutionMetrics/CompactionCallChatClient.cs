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
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionMetricsScope.MarkPurpose(ExecutionMetricsValues.Purpose.Compaction);
        return await innerClient.GetResponseAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var _ = ExecutionMetricsScope.MarkPurpose(ExecutionMetricsValues.Purpose.Compaction);
        await foreach (var update in innerClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        innerClient.GetService(serviceType, serviceKey);

    /// <summary>No-op de propósito: este tipo não é dono do client compartilhado.</summary>
    public void Dispose()
    {
    }
}
