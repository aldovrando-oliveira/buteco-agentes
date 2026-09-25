import type { DailyInsightPoint } from '../types/systemInsights';

// QUAIS DIAS FORAM MEDIDOS — LENDO A SÉRIE, E SÓ A SÉRIE.
//
// ESTE MÓDULO NÃO RECEBE `regimes`, E ISSO É A DECISÃO (D2), NÃO UM DESCUIDO.
//
// A rota emite um ponto para TODO dia medido, inclusive o dia medido em que nada
// aconteceu (`taskCount: 0`), e omite os dias anteriores ao início do regime.
// Logo a ausência de um dia na série significa UMA coisa: não foi medido. Essa
// é a metade que a #65 acrescentou, e é o que torna a distinção legível sem
// cruzar nada.
//
// Reconstruir "medido" a partir do mapa de regimes criaria DUAS regras — a do
// servidor e a do cliente — que divergem em silêncio: o regime é configuração do
// servidor, e o `02` já registra que ele pode ser declarado antes da medição
// real começar. Nesse caso o cliente emitiria 0 para dias que o servidor sabe
// não ter medido, com número plausível e sem sintoma.
//
// A ASSINATURA SEM `regimes` É O GUARDA. Não dá para "consultar o regime por
// descuido" um parâmetro que não existe, e o teste afirma isso por escrito —
// é o tipo de acoplamento que volta numa refatoração distraída se só houver
// comentário proibindo.
//
// O REGIME SERVE AO TEXTO "medindo desde", que é outro lugar e outra pergunta.

export type DayState = 'measured' | 'measured-zero' | 'unmeasured';

export interface MeasuredDay {
  /** `YYYY-MM-DD` no fuso da resposta. */
  day: string;
  state: DayState;
  /** A contagem do dia, ou `null` quando o dia não foi medido. */
  taskCount: number | null;
}

export interface MeasuredDays {
  /** Todos os dias da janela pedida, em ordem, no fuso da resposta. */
  days: MeasuredDay[];
  /** A maior contagem entre os dias MEDIDOS. `null` quando não há nenhum. */
  maxMeasuredCount: number | null;
  /** Os dias da semana (0 = domingo) que OCORREM entre os dias medidos. */
  coveredWeekdays: Set<number>;
}

// A CONVERSÃO DE INSTANTE PARA DIA USA O FUSO DA RESPOSTA, NUNCA O DO NAVEGADOR.
//
// `sv-SE` porque é o único locale que o `Intl` entrega já em `YYYY-MM-DD` — o
// mesmo formato que a rota usa em `DailyInsightPoint.day`, o que dispensa
// remontar a string por pedaços e errar o zero à esquerda.
//
// `toISOString().slice(0, 10)` seria UTC, e `getDate()` seria o fuso do
// navegador: os dois deslocam o dia para quem abre o painel fora de
// America/Sao_Paulo, e o deslocamento é de um dia inteiro perto da meia-noite.
function localDay(instant: Date, timeZone: string): string {
  return new Intl.DateTimeFormat('sv-SE', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(instant);
}

/** O dia da semana local de um `YYYY-MM-DD`. 0 = domingo, como `DayOfWeek`. */
export function weekdayOf(day: string): number {
  // `T00:00:00Z` e `getUTCDay()`: a data já é local (veio de `localDay` ou da
  // rota), então tratá-la como UTC puro é o que impede um segundo deslocamento.
  return new Date(`${day}T00:00:00Z`).getUTCDay();
}

/** Avança um `YYYY-MM-DD` em um dia, sem tocar em fuso. */
function nextDay(day: string): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}

export function measuredDays(
  window: { from: string; to: string; timeZone: string },
  dailySeries: DailyInsightPoint[],
): MeasuredDays {
  const byDay = new Map(dailySeries.map((point) => [point.day, point]));

  const first = localDay(new Date(window.from), window.timeZone);
  const last = localDay(new Date(window.to), window.timeZone);

  const days: MeasuredDay[] = [];
  for (let day = first; day <= last; day = nextDay(day)) {
    const point = byDay.get(day);
    if (point === undefined) {
      // AUSENTE DA SÉRIE = NÃO MEDIDO, e `taskCount` fica NULO. Devolver 0 aqui
      // inventaria um período de inatividade que nunca existiu — é o defeito
      // que a #65 corrigiu no servidor, e que voltaria pelo cliente.
      days.push({ day, state: 'unmeasured', taskCount: null });
      continue;
    }
    days.push({
      day,
      state: point.taskCount === 0 ? 'measured-zero' : 'measured',
      taskCount: point.taskCount,
    });
  }

  const measuredCounts = days
    .filter((d) => d.state !== 'unmeasured')
    .map((d) => d.taskCount as number);

  // A COBERTURA POR DIA DA SEMANA SAI DA SÉRIE, NÃO DO REGIME.
  //
  // `byWeekday` omite o dia da semana sem ocorrência exatamente como a série
  // diária omitia o dia vazio antes da #65 — mesmo defeito, e a spec da change
  // que criou a rota NÃO o cobre. Esta é a única reconstrução que sobra no
  // cliente, e a fonte dela continua sendo a série: um dia da semana que ocorre
  // entre os dias medidos e não aparece em `byWeekday` é zero medido; um que
  // não ocorre entre eles é NÃO MEDIDO, e não tem zero para dar.
  //
  // Está registrado no `02` como item aberto (D2): a #65 decide se estende o
  // recorte a M6 ou se a cobertura fica do cliente.
  const coveredWeekdays = new Set(
    days.filter((d) => d.state !== 'unmeasured').map((d) => weekdayOf(d.day)),
  );

  return {
    days,
    maxMeasuredCount: measuredCounts.length === 0 ? null : Math.max(...measuredCounts),
    coveredWeekdays,
  };
}
