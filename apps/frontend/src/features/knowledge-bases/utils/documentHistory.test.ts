import { describe, expect, it } from 'vitest';
import {
  authorLabel,
  eventDetail,
  eventTypeLabel,
  formatEventDay,
  formatEventTime,
  groupEventsByDay,
} from './documentHistory';
import type { KnowledgeDocumentEvent } from '../types/knowledgeDocumentEvent';

function evento(overrides: Partial<KnowledgeDocumentEvent> = {}): KnowledgeDocumentEvent {
  return {
    id: 'e-1',
    documentId: 'doc-1',
    documentTitle: 'Política de trocas',
    type: 'Created',
    contentChanged: null,
    titleChanged: null,
    author: 'operator',
    occurredAt: '2026-09-26T15:32:00Z',
    ...overrides,
  };
}

const KNOWN_LABELS = ['Incluído', 'Atualizado', 'Excluído'];

describe('eventTypeLabel', () => {
  it('cada tipo conhecido tem o seu rótulo', () => {
    expect(eventTypeLabel('Created')).toEqual({ label: 'Incluído', known: true });
    expect(eventTypeLabel('Updated')).toEqual({ label: 'Atualizado', known: true });
    expect(eventTypeLabel('Deleted')).toEqual({ label: 'Excluído', known: true });
  });

  // Convenção 13: valor de enum desconhecido vai para indicador neutro, e nunca
  // reaproveita o de um valor conhecido.
  it('tipo desconhecido volta com o valor recebido, marcado como desconhecido', () => {
    const result = eventTypeLabel('Archived');
    expect(result).toEqual({ label: 'Archived', known: false });
    expect(KNOWN_LABELS).not.toContain(result.label);
  });
});

describe('eventDetail', () => {
  it('Updated diz o que mudou', () => {
    expect(
      eventDetail(evento({ type: 'Updated', contentChanged: true, titleChanged: false })),
    ).toBe('Conteúdo alterado');
    expect(
      eventDetail(evento({ type: 'Updated', contentChanged: false, titleChanged: true })),
    ).toBe('Título alterado');
    expect(eventDetail(evento({ type: 'Updated', contentChanged: true, titleChanged: true }))).toBe(
      'Conteúdo e título alterados',
    );
  });

  it('Created e Deleted não têm detalhe', () => {
    expect(eventDetail(evento({ type: 'Created' }))).toBeNull();
    expect(eventDetail(evento({ type: 'Deleted' }))).toBeNull();
  });

  // Não é gravável (CHECK da #98); se chegar, a tela não afirma o que mudou.
  it('Updated sem nenhuma mudança não tem detalhe', () => {
    expect(
      eventDetail(evento({ type: 'Updated', contentChanged: false, titleChanged: false })),
    ).toBeNull();
  });
});

describe('authorLabel', () => {
  it('operador e sincronização', () => {
    expect(authorLabel('operator')).toBe('Operador');
    expect(authorLabel('service:connectors')).toBe('Sincronização');
  });

  it('autor desconhecido aparece como veio, e não como operador nem sincronização', () => {
    const label = authorLabel('service:outro');
    expect(label).toBe('Autor que o painel não reconhece (service:outro)');
    expect(label).not.toMatch(/^Operador$|^Sincronização$/);
  });

  it('autor vazio não vira operador', () => {
    expect(authorLabel('')).toBe('Autor não registrado');
  });
});

describe('formatEventDay e formatEventTime', () => {
  // O instante cruza a meia-noite entre os dois fusos: é o caso que separa
  // "agrupou no fuso" de "agrupou no UTC".
  const CROSSING = '2026-09-26T02:30:00Z';

  it('no fuso de São Paulo o dia é o anterior ao do UTC', () => {
    expect(formatEventDay(CROSSING, 'America/Sao_Paulo')).toBe('25/09/2026');
    expect(formatEventTime(CROSSING, 'America/Sao_Paulo')).toBe('23:30');
  });

  it('no UTC o dia é o do instante', () => {
    expect(formatEventDay(CROSSING, 'UTC')).toBe('26/09/2026');
    expect(formatEventTime(CROSSING, 'UTC')).toBe('02:30');
  });
});

describe('groupEventsByDay', () => {
  it('um grupo por dia, na ordem da entrada', () => {
    const groups = groupEventsByDay(
      [
        evento({ id: 'a', occurredAt: '2026-09-26T17:32:00Z' }),
        evento({ id: 'b', occurredAt: '2026-09-26T12:05:00Z' }),
        evento({ id: 'c', occurredAt: '2026-09-23T19:03:00Z' }),
      ],
      'America/Sao_Paulo',
    );

    expect(groups.map((group) => group.day)).toEqual(['26/09/2026', '23/09/2026']);
    expect(groups[0].events.map((event) => event.id)).toEqual(['a', 'b']);
    expect(groups[1].events.map((event) => event.id)).toEqual(['c']);
  });

  it('agrupa pelo dia do fuso dado', () => {
    const events = [evento({ occurredAt: '2026-09-26T02:30:00Z' })];
    expect(groupEventsByDay(events, 'America/Sao_Paulo')[0].day).toBe('25/09/2026');
    expect(groupEventsByDay(events, 'UTC')[0].day).toBe('26/09/2026');
  });

  // A tela agrupa a CONCATENAÇÃO das páginas: o último da primeira e o primeiro
  // da segunda, do mesmo dia, ficam num grupo só.
  it('um dia partido entre duas páginas vira um grupo só', () => {
    const firstPage = [
      evento({ id: 'a', occurredAt: '2026-09-26T17:32:00Z' }),
      evento({ id: 'b', occurredAt: '2026-09-25T20:00:00Z' }),
    ];
    const secondPage = [
      evento({ id: 'c', occurredAt: '2026-09-25T13:10:00Z' }),
      evento({ id: 'd', occurredAt: '2026-09-23T19:03:00Z' }),
    ];

    const groups = groupEventsByDay([...firstPage, ...secondPage], 'America/Sao_Paulo');

    expect(groups.map((group) => group.day)).toEqual(['26/09/2026', '25/09/2026', '23/09/2026']);
    expect(groups[1].events.map((event) => event.id)).toEqual(['b', 'c']);
  });

  it('nenhum cabeçalho é relativo', () => {
    const now = new Date().toISOString();
    const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
    const days = groupEventsByDay([
      evento({ id: 'a', occurredAt: now }),
      evento({ id: 'b', occurredAt: yesterday }),
    ]).map((group) => group.day);

    // Dois grupos, para a asserção abaixo não passar no vazio.
    expect(days).toHaveLength(2);
    for (const day of days) {
      expect(day).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
      expect(day).not.toMatch(/Hoje|Ontem/);
    }
  });
});
