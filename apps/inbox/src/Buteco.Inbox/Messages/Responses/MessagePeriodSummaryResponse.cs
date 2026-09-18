namespace Buteco.Inbox.Messages.Responses;

/// <summary>
/// Contagem de mensagens de <b>entrada</b> recebidas no período — não "mensagens
/// no período", que incluiria as de saída (design.md, D4 e D6).
/// </summary>
/// <remarks>
/// O campo se chama <c>InboundCount</c>, e não <c>ReceivedCount</c>, de propósito
/// (design.md, D4). O argumento <b>não é o mesmo</b> de <c>StartedCount</c> em
/// <see cref="Contacts.Responses.SessionPeriodSummaryResponse"/>: lá o problema
/// era vagueza (<c>Count</c> não afirma nada); aqui é <b>ambiguidade de ponto de
/// vista</b>. "Mensagem recebida" é, do sistema, a que o contato mandou; do
/// contato, é a que o agente mandou — e o rótulo de tela, que fixaria o
/// referencial, ainda não existe. <c>Inbound</c> herda o vocabulário de
/// <see cref="Entities.MessageDirection"/>, definido em relação ao sistema, e não
/// admite a inversão.
///
/// Registro em objeto, e não um inteiro nu no corpo, para admitir o segundo campo
/// sem quebrar contrato. Esse campo já tem nome e endereço conhecidos:
/// <c>OutboundCount</c>, adiado em D6 junto com a pergunta que ele carrega — uma
/// entrega com <c>DeliveryStatus = Failed</c> conta como enviada?
///
/// Sem <c>FromEntity</c>, ao contrário de <see cref="MessageResponse"/>: não há
/// entidade de origem, é uma agregação.
/// </remarks>
public sealed record MessagePeriodSummaryResponse(int InboundCount);
