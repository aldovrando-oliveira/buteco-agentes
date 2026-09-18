// A JANELA DOS ITENS DE ATIVIDADE: OS 7 DIAS QUE TERMINAM NO INSTANTE DA CONSULTA.
//
// As rotas `/sessions/summary` e `/messages/summary` de apps/inbox exigem `from`
// e `to` e recusam período implícito — a janela é responsabilidade de quem
// pergunta, e é esta função (design.md, D3).
//
// ROLANTE, E NÃO DE CALENDÁRIO. "Desde a meia-noite de 6 dias atrás" obrigaria a
// decidir de qual fuso é a meia-noite, e a janela mudaria de tamanho com o
// horário de verão. Aqui são 7 × 24h de relógio absoluto, sempre as mesmas 168h.
//
// SEM CONVERSÃO MANUAL PARA UTC. `toISOString()` sempre emite UTC com `Z`; o
// backend aceitaria offset local também, mas com esta saída a pergunta não se
// coloca.
//
// "AGORA" É PARÂMETRO. Nada de `new Date()` aqui dentro: quem chama é o `queryFn`
// do hook, no instante da consulta — e é isso que faz a nova tentativa consultar
// a janela atualizada, e não uma calculada antes.

const SEVEN_DAYS_MS = 7 * 24 * 60 * 60 * 1000;

export function lastSevenDaysWindow(now: Date): { from: string; to: string } {
  return {
    from: new Date(now.getTime() - SEVEN_DAYS_MS).toISOString(),
    to: now.toISOString(),
  };
}
