using System.Diagnostics;
using System.Runtime.CompilerServices;
using Buteco.Workers.ExecutionMetrics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Agents;

/// <summary>
/// Registra em log a duração de cada requisição ao provedor de LLM.
///
/// <para>
/// <b>Por que existe.</b> Quando o vazamento de pool de conexões corrigido pela
/// change <c>fix-vazamento-httpclient-chat</c> foi diagnosticado, a única pista
/// disponível era um stack trace estourando em 100 s dentro de
/// <c>HttpConnectionPool.SendWithVersionDetectionAndRetryAsync</c> — espera por
/// conexão do pool, não por resposta do servidor. Uma série de durações por
/// requisição teria mostrado a degradação subindo mensagem a mensagem. Sem esse
/// instrumento, o próximo diagnóstico do mesmo tipo custa o mesmo.
/// </para>
///
/// <para>
/// <b>Mede a requisição ao provedor, não o turno do agente.</b>
/// <c>ChatClientAgent</c> empilha o middleware dele — incluindo
/// <c>FunctionInvokingChatClient</c> — POR FORA do client que recebe (via
/// <c>WithDefaultAgentMiddleware</c>). Como <see cref="ChatClientResolver"/>
/// compõe este wrapper junto do client do SDK, ele fica na camada mais interna:
/// cada linha registrada é UMA requisição HTTP ao provedor, e o tempo de
/// execução das tools MCP fica de fora. Era exatamente essa separação que
/// faltava — cronometrar <c>aiAgent.RunAsync</c> teria dado um número que sobe
/// sem dizer qual parte subiu.
/// </para>
///
/// <para>
/// <b>Registra também no caminho de falha</b>, antes de a exceção propagar: uma
/// chamada que estoura por espera de pool é justamente o caso que não pode ficar
/// sem medida.
/// </para>
///
/// <para>
/// <b>É também o assento da linha filha das métricas de execução</b> (change
/// <c>metricas-execucao-coleta</c>): pela mesma posição — a camada mais interna —
/// é o único ponto que vê cada requisição separada do tempo das tools, inclusive
/// a de compactação, que <c>AgentResponse.Usage</c> não inclui. Quem é a task
/// ele descobre pelo <see cref="ExecutionMetricsScope"/> ambiente, não por
/// parâmetro: este client é compartilhado por <c>(provider, model)</c> durante a
/// vida do processo. Fora de execução, o registro é no-op. Os tokens vão como o
/// provedor os reportou — nulo continua nulo.
/// </para>
/// </summary>
public sealed class LlmCallDurationChatClient(
    IChatClient innerClient,
    string provider,
    string model,
    ILogger<LlmCallDurationChatClient> logger) : DelegatingChatClient(innerClient)
{
    /// <summary>
    /// Client do SDK do provedor que este wrapper envolve.
    ///
    /// <para>
    /// Público porque <c>InnerClient</c> de <c>DelegatingChatClient</c> é
    /// <c>protected</c> e a composição é parte do que os testes de
    /// <see cref="ChatClientResolver"/> afirmam: qual client é construído para
    /// qual provedor. Sem este acessor a asserção teria que cair no tipo do
    /// wrapper, que é a mesma resposta para os três provedores.
    /// </para>
    /// </summary>
    public IChatClient InnerChatClient => InnerClient;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        ChatResponse? response = null;
        Exception? failure = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            LogCall(elapsed, streaming: false, failure);
            RecordCall(elapsed, response?.Usage, failure);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A medida cobre a enumeração inteira, não só a obtenção do enumerador:
        // no caminho de streaming é a leitura dos updates que ocupa a conexão.
        //
        // No streaming o uso chega como UsageContent dentro dos updates, e é
        // somado com a mesma semântica de nulo de UsageDetails.Add. Sem bloco
        // `catch` aqui (C# não permite `yield` dentro de try com catch): uma
        // enumeração que não chegou ao fim — por exceção ou por abandono de quem
        // consumia — é registrada como falha.
        var started = Stopwatch.GetTimestamp();
        UsageDetails? usage = null;
        var completed = false;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                foreach (var usageContent in update.Contents.OfType<UsageContent>())
                {
                    (usage ??= new UsageDetails()).Add(usageContent.Details);
                }

                yield return update;
            }

            completed = true;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);

            // Sem exceção em mãos no streaming (C# não permite `catch` com
            // `yield`), então a linha de falha sai sem tipo: "não completou" é
            // tudo o que este caminho sabe, e inventar mais seria pior.
            LogCall(elapsed, streaming: true, failure: null, completed: completed);
            ExecutionMetricsScope.RecordProviderCall(
                provider, model, elapsed.TotalMilliseconds, usage, failed: !completed, httpStatus: null);
        }
    }

    private void RecordCall(TimeSpan elapsed, UsageDetails? usage, Exception? failure) =>
        ExecutionMetricsScope.RecordProviderCall(
            provider,
            model,
            elapsed.TotalMilliseconds,
            usage,
            failed: failure is not null,
            httpStatus: failure is null ? null : ExecutionMetricsScope.HttpStatusOf(failure));

    /// <summary>
    /// Escreve a linha da requisição — <b>sucesso e falha em linhas
    /// DISTINTAS</b> (change <c>compactacao-historico</c>, D7).
    ///
    /// <para>
    /// <b>Por que deixou de ser uma linha só.</b> Até esta change o
    /// <c>finally</c> escrevia <i>"Chamada ao LLM concluída"</i> para os dois
    /// casos: "concluída" queria dizer só "terminou". No piloto, as seis
    /// chamadas de compactação que falharam apareceram no log com essa mesma
    /// linha, enquanto a tabela as gravava com <c>Failed = true</c> — e o
    /// diagnóstico começou tendo que decidir qual dos dois instrumentos estava
    /// errado.
    /// </para>
    ///
    /// <para>
    /// <b>Task e finalidade saem do escopo ambiente</b>, não de parâmetro, pelo
    /// mesmo motivo da linha filha (D2 da <c>metricas-execucao-coleta</c>): este
    /// client é compartilhado por <c>(provider, model)</c> e não sabe de quem é
    /// a chamada. Fora de execução, os dois saem vazios e a linha continua
    /// saindo — casar log com tabela não pode depender de durações únicas, que
    /// foi o que este diagnóstico teve de usar.
    /// </para>
    /// </summary>
    private void LogCall(TimeSpan elapsed, bool streaming, Exception? failure, bool completed = true)
    {
        var taskId = ExecutionMetricsScope.Current?.TaskId ?? string.Empty;
        var purpose = ExecutionMetricsScope.CurrentPurposeOrDefault;

        if (failure is null && completed)
        {
            logger.LogInformation(
                "Chamada ao LLM bem-sucedida: provider={Provider} model={Model} streaming={Streaming} "
              + "duracaoMs={DuracaoMs} taskId={TaskId} finalidade={Purpose}",
                provider,
                model,
                streaming,
                elapsed.TotalMilliseconds,
                taskId,
                purpose);

            return;
        }

        logger.LogWarning(
            "Chamada ao LLM falhou: provider={Provider} model={Model} streaming={Streaming} "
          + "duracaoMs={DuracaoMs} taskId={TaskId} finalidade={Purpose} "
          + "excecao={ExceptionType} httpStatus={HttpStatus}",
            provider,
            model,
            streaming,
            elapsed.TotalMilliseconds,
            taskId,
            purpose,
            failure?.GetType().Name ?? "EnumeracaoIncompleta",
            failure is null ? null : ExecutionMetricsScope.HttpStatusOf(failure));
    }
}
