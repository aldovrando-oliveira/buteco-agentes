namespace Buteco.Inbox.Contacts.Responses;

/// <summary>
/// Contagem de sessões <b>iniciadas</b> no período — não "com atividade no
/// período", não "vivas durante o período" (design.md, D3).
/// </summary>
/// <remarks>
/// O campo se chama <c>StartedCount</c>, e não <c>Count</c>, de propósito
/// (design.md, D4): é a convenção 13 aplicada ao contrato da API. Um campo
/// chamado <c>Count</c> numa rota chamada <c>/sessions/summary</c> não afirma
/// nada, o que o torna re-lível como "sessões ativas" pelo próximo consumidor —
/// e o rótulo de tela, que seria o lugar natural de corrigir essa leitura, ainda
/// não existe.
///
/// Registro em objeto, e não um inteiro nu no corpo, pelo mesmo motivo: um
/// <c>200</c> com corpo <c>7</c> não tem onde pendurar o nome, e fecha a porta
/// para um segundo campo (p.ex. <c>ClosedCount</c>, se o encerramento explícito
/// de sessão existir um dia) sem quebrar o contrato.
///
/// Sem <c>FromEntity</c>, ao contrário de <see cref="SessionResponse"/> e
/// <see cref="ContactResponse"/>: não há entidade de origem, é uma agregação.
/// </remarks>
public sealed record SessionPeriodSummaryResponse(int StartedCount);
