import { describe, expect, it } from 'vitest';
import { formatDate, formatRelative } from './accountDates';

const SP = 'America/Sao_Paulo';

describe('accountDates', () => {
  it('hoje, ontem e antes, no fuso', () => {
    const now = new Date('2026-11-06T18:00:00Z'); // 15:00 em São Paulo

    expect(formatRelative('2026-11-06T12:02:00Z', now, SP)).toBe('hoje, 09:02');
    expect(formatRelative('2026-11-06T00:15:00Z', now, SP)).toBe('ontem, 21:15');
    expect(formatRelative('2026-10-28T21:30:00Z', now, SP)).toBe('28/10/2026, 18:30');
  });

  it('a virada do dia é a do fuso, e não a do UTC', () => {
    const now = new Date('2026-11-06T04:00:00Z'); // 01:00 de 06/11 em São Paulo

    // 23:30 de 05/11 em São Paulo é 02:30 de 06/11 em UTC: o mesmo dia do agora em UTC.
    expect(formatRelative('2026-11-06T02:30:00Z', now, SP)).toBe('ontem, 23:30');
  });

  it('a data só com dia, mês e ano', () => {
    expect(formatDate('2026-08-12T13:00:00Z', SP)).toBe('12/08/2026');
  });
});
