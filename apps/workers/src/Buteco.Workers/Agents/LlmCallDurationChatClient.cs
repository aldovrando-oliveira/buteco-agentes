using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        finally
        {
            LogDuration(started, streaming: false);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A medida cobre a enumeração inteira, não só a obtenção do enumerador:
        // no caminho de streaming é a leitura dos updates que ocupa a conexão.
        var started = Stopwatch.GetTimestamp();
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            LogDuration(started, streaming: true);
        }
    }

    private void LogDuration(long startedTimestamp, bool streaming)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedTimestamp);

        logger.LogInformation(
            "Chamada ao LLM concluída: provider={Provider} model={Model} streaming={Streaming} duracaoMs={DuracaoMs}",
            provider,
            model,
            streaming,
            elapsed.TotalMilliseconds);
    }
}
