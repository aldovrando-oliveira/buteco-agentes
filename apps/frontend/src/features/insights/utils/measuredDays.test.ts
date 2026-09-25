import { describe, expect, it } from 'vitest';
import { measuredDays, weekdayOf } from './measuredDays';
import type { DailyInsightPoint } from '../types/systemInsights';

const janela = {
  // 2026-09-15 00:00 a 2026-09-24 23:59, em America/Sao_Paulo.
  from: '2026-09-15T03:00:00+00:00',
  to: '2026-09-25T02:59:59+00:00',
  timeZone: 'America/Sao_Paulo',
};

/** A série que a rota real serviu em 24/09: só os dias dentro do regime. */
const serieReal: DailyInsightPoint[] = [
  { day: '2026-09-22', taskCount: 0, tokenCount: null },
  { day: '2026-09-23', taskCount: 0, tokenCount: null },
  { day: '2026-09-24', taskCount: 0, tokenCount: null },
];

describe('measuredDays', () => {
  it('cobre todos os dias da janela pedida', () => {
    const { days } = measuredDays(janela, serieReal);

    expect(days).toHaveLength(10);
    expect(days[0].day).toBe('2026-09-15');
    expect(days[9].day).toBe('2026-09-24');
  });

  it('dia presente na série com contagem é medido', () => {
    const { days } = measuredDays(janela, [
      { day: '2026-09-22', taskCount: 26, tokenCount: 900_000 },
    ]);

    const dia22 = days.find((d) => d.day === '2026-09-22');
    expect(dia22).toEqual({ day: '2026-09-22', state: 'measured', taskCount: 26 });
  });

  it('dia presente com 0 vira ZERO MEDIDO, não vazio nem não medido', () => {
    const { days } = measuredDays(janela, serieReal);

    const dia22 = days.find((d) => d.day === '2026-09-22');
    expect(dia22?.state).toBe('measured-zero');
    expect(dia22?.state).not.toBe('unmeasured');
    expect(dia22?.taskCount).toBe(0);
  });

  it('dia AUSENTE da série vira não medido', () => {
    const { days } = measuredDays(janela, serieReal);

    const anteriores = days.filter((d) => d.day < '2026-09-22');
    expect(anteriores).toHaveLength(7);
    expect(anteriores.every((d) => d.state === 'unmeasured')).toBe(true);
  });

  it('NEGATIVO: nenhum dia ausente da série devolve contagem', () => {
    // O guarda que importa. Um `?? 0` aqui dentro inventaria sete dias de
    // inatividade que nunca existiram — e o número seria pequeno, plausível e
    // sem sintoma nenhum na tela.
    const { days } = measuredDays(janela, serieReal);

    const naoMedidos = days.filter((d) => d.state === 'unmeasured');
    expect(naoMedidos).toHaveLength(7);
    for (const dia of naoMedidos) {
      expect(dia.taskCount).toBeNull();
      expect(dia.taskCount).not.toBe(0);
    }
  });

  it('o máximo medido ignora os dias não medidos', () => {
    const { maxMeasuredCount } = measuredDays(janela, [
      { day: '2026-09-22', taskCount: 26, tokenCount: null },
      { day: '2026-09-23', taskCount: 31, tokenCount: null },
    ]);

    expect(maxMeasuredCount).toBe(31);
  });

  it('sem nenhum dia medido, o máximo é nulo e não zero', () => {
    const { maxMeasuredCount } = measuredDays(janela, []);

    expect(maxMeasuredCount).toBeNull();
    expect(maxMeasuredCount).not.toBe(0);
  });

  // ------------------------------------------------------------------ FUSO

  it('o dia não desloca quando o navegador está em outro fuso', () => {
    // A conversão usa `window.timeZone`, e não o fuso do processo. Se usasse
    // `getDate()` ou `toISOString()`, a borda de meia-noite deslocaria o dia
    // inteiro para quem abre o painel fora de America/Sao_Paulo.
    //
    // 2026-09-22T02:30:00Z é 2026-09-21 23:30 em São Paulo — dia 21, não 22.
    const janelaDeBorda = {
      from: '2026-09-22T02:30:00+00:00',
      to: '2026-09-22T03:30:00+00:00',
      timeZone: 'America/Sao_Paulo',
    };

    const { days } = measuredDays(janelaDeBorda, []);

    expect(days.map((d) => d.day)).toEqual(['2026-09-21', '2026-09-22']);
  });

  it('o mesmo instante em outro fuso da resposta dá outro dia', () => {
    const emToquio = measuredDays(
      { from: '2026-09-22T02:30:00+00:00', to: '2026-09-22T02:30:00+00:00', timeZone: 'Asia/Tokyo' },
      [],
    );

    // 02:30Z é 11:30 do dia 22 em Tóquio, e 23:30 do dia 21 em São Paulo.
    expect(emToquio.days.map((d) => d.day)).toEqual(['2026-09-22']);
  });

  // --------------------------------------------- GUARDA DE ACOPLAMENTO (D2)

  it('a função é assinada SEM regimes — e é isso que impede a reconstrução voltar', () => {
    // Não é teste de tipo por preciosismo: a reconstrução no cliente é a coisa
    // que a D2 recusou, e ela volta por descuido numa refatoração se o módulo
    // tiver acesso ao mapa de regimes. Não dá para consultar um parâmetro que
    // não existe, e este caso é o que torna a ausência dele deliberada por
    // escrito em vez de acidental.
    expect(measuredDays).toHaveLength(2);

    // E a prova de comportamento: uma série que CONTRADIZ o regime é seguida
    // mesmo assim. Aqui o dia 16 tem ponto embora o regime declarado comece em
    // 22 — a apresentação segue a série, porque é ela que sabe o que foi medido.
    const { days } = measuredDays(janela, [
      { day: '2026-09-16', taskCount: 3, tokenCount: null },
    ]);

    expect(days.find((d) => d.day === '2026-09-16')?.state).toBe('measured');
  });

  // ------------------------------------------------- COBERTURA POR DIA DA SEMANA

  it('série de dois dias cobre dois dias da semana, e os outros cinco não', () => {
    // 2026-09-22 é terça (2) e 2026-09-23 é quarta (3).
    const { coveredWeekdays } = measuredDays(janela, [
      { day: '2026-09-22', taskCount: 0, tokenCount: null },
      { day: '2026-09-23', taskCount: 0, tokenCount: null },
    ]);

    expect([...coveredWeekdays].sort()).toEqual([2, 3]);

    // NEGATIVO: os outros cinco não estão cobertos, e por isso não têm zero
    // para dar — nunca houve medição deles.
    for (const dia of [0, 1, 4, 5, 6]) {
      expect(coveredWeekdays.has(dia)).toBe(false);
    }
  });

  it('a cobertura inclui o dia da semana medido e vazio', () => {
    // Medido e vazio é MEDIDO: o dia da semana dele está coberto e sai como
    // zero medido, não como não medido.
    const { coveredWeekdays } = measuredDays(janela, serieReal);

    expect([...coveredWeekdays].sort()).toEqual([2, 3, 4]);
  });

  it('weekdayOf usa o mesmo mapeamento de DayOfWeek', () => {
    expect(weekdayOf('2026-09-20')).toBe(0); // domingo
    expect(weekdayOf('2026-09-22')).toBe(2); // terça
    expect(weekdayOf('2026-09-26')).toBe(6); // sábado
  });
});
