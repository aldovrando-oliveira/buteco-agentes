using Microsoft.Extensions.AI;

namespace Buteco.Workers.Agents;

/// <summary>
/// Mantém a primeira mensagem de sistema (se houver) e as
/// <paramref name="maxMessages"/> mensagens não-system mais recentes.
/// </summary>
/// <remarks>
/// Mesma semântica do <c>MessageCountingChatReducer</c> nativo do
/// <c>Microsoft.Extensions.AI</c>, reimplementada aqui porque aquele tipo é
/// <c>[Experimental("MEAI001")]</c> (sujeito a mudança ou remoção em
/// atualizações futuras), enquanto <see cref="IChatReducer"/> — a interface
/// que ele implementa, usada por
/// <c>InMemoryChatHistoryProviderOptions.ChatReducer</c> — não é. Ver
/// design.md da change apps-workers-historico-conversa, Decisão 4.
/// Simplificado em relação ao original: não exclui mensagens de
/// function call/result, porque nenhum agente usa tools/MCP ainda
/// (non-goal explícito da change backend-agente-a2a-mvp) — não há esse tipo
/// de conteúdo para excluir hoje.
/// </remarks>
public sealed class RecentMessageChatReducer(int maxMessages) : IChatReducer
{
    public Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ChatMessage? firstSystemMessage = null;
        var recent = new Queue<ChatMessage>(maxMessages);

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                firstSystemMessage ??= message;
                continue;
            }

            if (recent.Count >= maxMessages)
            {
                recent.Dequeue();
            }

            recent.Enqueue(message);
        }

        return Task.FromResult(Reduce(firstSystemMessage, recent));
    }

    private static IEnumerable<ChatMessage> Reduce(ChatMessage? firstSystemMessage, Queue<ChatMessage> recent)
    {
        if (firstSystemMessage is not null)
        {
            yield return firstSystemMessage;
        }

        foreach (var message in recent)
        {
            yield return message;
        }
    }
}
