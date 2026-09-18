import { describe, expect, it } from 'vitest';
import { lastSevenDaysWindow } from './activityWindow';

const SEVEN_DAYS_MS = 604_800_000;

// Hora de relógio num fuso EXPLÍCITO, independente do fuso do processo.
function wallClockHour(iso: string, timeZone: string): number {
  return Number(
    new Intl.DateTimeFormat('en-US', { timeZone, hour: 'numeric', hourCycle: 'h23' }).format(
      new Date(iso),
    ),
  );
}

describe('lastSevenDaysWindow', () => {
  const now = new Date('2026-09-18T14:30:15.250Z');

  it('termina exatamente no instante informado', () => {
    expect(lastSevenDaysWindow(now).to).toBe('2026-09-18T14:30:15.250Z');
  });

  it('começa exatamente 7 × 24h antes do fim', () => {
    const { from, to } = lastSevenDaysWindow(now);

    expect(from).toBe('2026-09-11T14:30:15.250Z');
    expect(Date.parse(to) - Date.parse(from)).toBe(SEVEN_DAYS_MS);
  });

  it('entrega os dois limites em UTC', () => {
    const { from, to } = lastSevenDaysWindow(now);

    expect(from).toMatch(/Z$/);
    expect(to).toMatch(/Z$/);
  });

  it('não depende de relógio: o mesmo instante dá sempre a mesma janela', () => {
    expect(lastSevenDaysWindow(new Date(now))).toEqual(lastSevenDaysWindow(new Date(now)));
  });

  // O processo desta máquina roda em America/Sao_Paulo, que não tem horário de
  // verão desde 2019 — um teste no fuso local passaria sem atravessar virada
  // nenhuma. Por isso a virada é real e datada (Nova York adiantou o relógio em
  // 08/03/2026, 02:00 → 03:00), e a hora de relógio é lida com fuso explícito.
  it('atravessa a virada de horário de verão e continua com 168h', () => {
    const afterSpringForward = new Date('2026-03-12T16:00:00.000Z'); // 12:00 EDT
    const { from, to } = lastSevenDaysWindow(afterSpringForward);

    expect(from).toBe('2026-03-05T16:00:00.000Z'); // 11:00 EST
    expect(Date.parse(to) - Date.parse(from)).toBe(SEVEN_DAYS_MS);

    // Prova de que a janela realmente atravessou a virada: no relógio de Nova
    // York, o início e o fim NÃO caem na mesma hora do dia. Uma janela "de
    // calendário" daria a mesma hora e 167h; a rolante dá 168h e hora diferente.
    expect(wallClockHour(from, 'America/New_York')).toBe(11);
    expect(wallClockHour(to, 'America/New_York')).toBe(12);
  });
});
