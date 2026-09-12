using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// <see cref="IChatClient"/> falso que registra o próprio descarte e passa a
/// lançar <see cref="ObjectDisposedException"/> depois dele.
///
/// <para>
/// Existe para o guarda de R1 do <c>design.md</c> da change
/// <c>fix-vazamento-httpclient-chat</c>: com o client cacheado por
/// <c>(provider, model)</c> e compartilhado entre execuções, qualquer descarte
/// passa a afetar TODAS as mensagens seguintes daquele par. Um
/// <c>Mock&lt;IChatClient&gt;</c> não serve aqui — o <c>Dispose()</c> de um mock
/// é inócuo, então ele passaria verde com o descarte em cascata presente.
/// </para>
///
/// <para>
/// O descarte em cascata é real e verificado: <c>DelegatingChatClient.Dispose(bool)</c>
/// chama <c>InnerClient.Dispose()</c>, e <c>ChatClientAgent</c> empilha middleware
/// delegante sobre o client recebido (<c>WithDefaultAgentMiddleware</c>). Hoje nada
/// descarta o agente — <c>ChatClientAgent</c> não é <c>IDisposable</c> —, e é
/// exatamente essa garantia que este falso prende.
/// </para>
/// </summary>
public sealed class DisposalTrackingChatClient(string replyText) : IChatClient
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, replyText)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
        return Stream();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, replyText);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => Interlocked.Increment(ref _disposeCount);
}
