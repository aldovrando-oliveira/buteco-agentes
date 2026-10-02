// A JANELA DA PÁGINA DE INSIGHTS: N × 24 H TERMINANDO NO INSTANTE DA CONSULTA.
//
// Mesma razão da `lastSevenDaysWindow` de
// `features/inventory/utils/activityWindow.ts`, e a razão está lá por escrito —
// aqui o que muda é só o período ser escolhido pelo operador em vez de fixo em
// 7 dias.
//
// ROLANTE, E NÃO DE CALENDÁRIO. "Desde a meia-noite de 29 dias atrás" obrigaria
// a decidir de qual fuso é a meia-noite, e a janela mudaria de tamanho com o
// horário de verão. Aqui são N × 24 h de relógio absoluto.
//
// "AGORA" É PARÂMETRO. Nada de `new Date()` aqui dentro: quem chama é o
// `queryFn` do hook, no instante da consulta — e é isso que faz a nova tentativa
// consultar a janela atualizada, e não uma calculada antes (D4).
//
// SEM CONVERSÃO MANUAL PARA UTC: `toISOString()` sempre emite UTC com `Z`.

export const INSIGHTS_PERIODS = ['7d', '30d', '90d'] as const;

export type InsightsPeriod = (typeof INSIGHTS_PERIODS)[number];

export const DEFAULT_INSIGHTS_PERIOD: InsightsPeriod = '30d';

const DAY_MS = 24 * 60 * 60 * 1000;

const PERIOD_DAYS: Record<InsightsPeriod, number> = {
  '7d': 7,
  '30d': 30,
  '90d': 90,
};

export function periodDays(period: InsightsPeriod): number {
  return PERIOD_DAYS[period];
}

// O PERÍODO VEM DO ENDEREÇO, E O QUE O ENDEREÇO CARREGA É O NOME DA JANELA.
//
// `?period=90d`, nunca `?from=…&to=…`. As duas formas são defensáveis e
// significam COISAS DIFERENTES, então a escolha precisa do motivo escrito:
//
//   - instantes absolutos CONGELAM a janela. Um link compartilhado amanhã
//     responde sobre o mesmo intervalo de ontem — a pergunta "o que aconteceu
//     naqueles 90 dias";
//   - o nome da janela DESLIZA. Compartilhado amanhã, responde sobre os últimos
//     90 dias de amanhã — a pergunta "como está indo".
//
// TRÊS RAZÕES ESCOLHEM A SEGUNDA, e a primeira é dirimente:
//
//   1. A janela rolante é REQUISITO VIVO, não preferência. `system-insights-ui`
//      exige "N × 24 h terminando no instante da consulta" e tem o cenário
//      "nova tentativa consulta a janela atualizada". Instantes no endereço
//      congelariam a janela e contradiriam os dois — a nova tentativa passaria a
//      reconsultar o intervalo velho. Escolher instantes aqui exigiria mudar
//      aquele requisito, o que é outra change e outra decisão.
//   2. A INTERFACE NÃO PRODUZ NEM LÊ DE VOLTA um limite arbitrário. O seletor é
//      um `SegmentedControl` de três opções (`PeriodPicker.tsx`). Um endereço com
//      instantes só nasceria de um retrato de um preset, e a volta — instantes →
//      segmento marcado — NÃO FECHA O CÍRCULO: 43 dias não corresponde a nenhum
//      dos três botões, e a tela teria de decidir qual marcar. Isso é interface
//      que não existe, inventada para servir um formato de endereço.
//   3. O preset é EXATAMENTE o que a chave de cache já carrega.
//      `useSystemInsights.ts` e `useAgentInsights.ts` põem o NOME do período na
//      chave, de propósito e com a razão escrita: janela na chave gera chave nova
//      a cada render e prende a tela em carregamento. Ler o preset do endereço
//      alimenta aquele mesmo eixo SEM TOCAR NA CHAVE.
//
// O CUSTO, DECLARADO: um link colado hoje e aberto na semana que vem mostra
// outros números. É o que a tela já significa — o seletor diz "90d", não
// "01/07 a 29/09" — e as duas superfícies ecoam a janela que a RESPOSTA devolve,
// então o intervalo concreto está sempre na tela de quem abre.
//
// AUSÊNCIA É A FORMA CANÔNICA DO PADRÃO, E DESCONHECIDO CAI NELA TAMBÉM — sem
// reescrever o endereço, que só poluiria o histórico. É o mesmo contrato que
// `parseTab` (`AgentDetailPage.tsx`) já declara para a aba, e é por simetria
// medida, não por conveniência: aquela página já trata identificação de aba
// desconhecida assim, com cenário de spec próprio. Duas chaves do mesmo endereço
// com disciplinas opostas — uma tolerante, uma que grita — seriam duas regras
// para o operador aprender sobre a mesma barra de endereços.
//
// E o silêncio não esconde nada, porque a tela já diz a verdade ao lado: a janela
// consultada vai para o cabeçalho ecoada pela resposta, então o operador VÊ em
// qual período está, qualquer que tenha sido o endereço.
//
// SEM `Record` NOVO DE EXAUSTIVIDADE, e é decisão (D9). A convenção 25 manda
// amarrar união a `Record` no teste, e AVISA que lista paralela não é
// exaustividade. Aqui a armadilha não existe por construção: `InsightsPeriod` é
// `(typeof INSIGHTS_PERIODS)[number]` — DERIVADA da lista, não declarada ao lado
// dela —, e `PERIOD_DAYS`/`PERIOD_LABELS` já são `Record<InsightsPeriod, …>`, que
// não compilam até o membro novo ser classificado. O caso da #89 era união
// escrita à mão com array paralelo, e NÃO é esta forma. Validar por pertencimento
// à própria lista faz um membro novo ser aceito no endereço sem nenhuma edição —
// que é o comportamento certo, não um esquecimento.
export function parsePeriod(value: string | null): InsightsPeriod {
  return INSIGHTS_PERIODS.includes(value as InsightsPeriod)
    ? (value as InsightsPeriod)
    : DEFAULT_INSIGHTS_PERIOD;
}

export const PERIOD_LABELS: Record<InsightsPeriod, string> = {
  '7d': '7d',
  '30d': '30d',
  '90d': '90d',
};

export function insightsWindow(
  period: InsightsPeriod,
  now: Date,
): { from: string; to: string } {
  return {
    from: new Date(now.getTime() - PERIOD_DAYS[period] * DAY_MS).toISOString(),
    to: now.toISOString(),
  };
}
