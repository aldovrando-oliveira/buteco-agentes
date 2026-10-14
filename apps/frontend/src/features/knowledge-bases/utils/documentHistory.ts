import type { KnowledgeDocumentEvent } from '../types/knowledgeDocumentEvent';

// RÓTULOS E AGRUPAMENTO DA ABA HISTÓRICO (design.md da change
// frontend-aba-historico, D2, D3 e D7).
//
// Este módulo fica FORA de syncStateMessages.ts e connectorErrors.ts de
// propósito (D5): aquelas fontes são varridas por `exclu|remov|apag`, porque um
// texto de falha nunca manda excluir a base. `Excluído` aqui registra um fato
// gravado pelo apps/api, não orienta ação — e a varredura não precisa de exceção
// porque estes rótulos não moram lá. Nenhum dos dois lados importa o outro.

export type EventTypeLabel = { label: string; known: boolean };

// Tipo desconhecido volta com o valor recebido e `known: false`, para o
// indicador neutro: nunca reaproveita o rótulo de um tipo conhecido
// (convenção 13).
export function eventTypeLabel(type: string): EventTypeLabel {
  switch (type) {
    case 'Created':
      return { label: 'Incluído', known: true };
    case 'Updated':
      return { label: 'Atualizado', known: true };
    case 'Deleted':
      return { label: 'Excluído', known: true };
    default:
      return { label: type, known: false };
  }
}

// Só `Updated` tem detalhe. `Updated` sem nenhuma das duas mudanças não é
// gravável (CHECK da #98); se chegasse, a tela diria `Atualizado` sem afirmar o
// que mudou. Nunca nomeia título anterior nem motivo de exclusão, que a rota não
// devolve (correções C1 e C2).
export function eventDetail(
  event: Pick<KnowledgeDocumentEvent, 'type' | 'contentChanged' | 'titleChanged'>,
): string | null {
  if (event.type !== 'Updated') return null;
  const content = event.contentChanged === true;
  const title = event.titleChanged === true;
  if (content && title) return 'Conteúdo e título alterados';
  if (content) return 'Conteúdo alterado';
  if (title) return 'Título alterado';
  return null;
}

// Subjects lidos no apps/api: `operator` é o que o login emite
// (AuthEndpoints.cs, tokenService.Issue("operator", ...)); `service:connectors` é
// o das escritas da sincronização (requisito de autor de
// knowledge-document-history). GATILHO: um subject novo que escreva documento —
// até o painel aprendê-lo, ele cai no texto neutro com o valor gravado, e nunca
// num dos dois rótulos. Por isso não há mapeamento por prefixo `service:`
// (`service:inbox` existe e não é a sincronização).
export function authorLabel(author: string): string {
  if (author === 'operator') return 'Operador';
  if (author === 'service:connectors') return 'Sincronização';
  if (author === '') return 'Autor não registrado';
  return `Autor que o painel não reconhece (${author})`;
}

const DAY_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
};
const TIME_FORMAT_OPTIONS: Intl.DateTimeFormatOptions = {
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
};

// FUSO DO NAVEGADOR, como toda data desta feature (formatSyncInstant,
// KnowledgeDocumentsCard). `timeZone` só existe para o teste fixar o fuso e
// atravessar a meia-noite; a tela não o passa (D7).
//
// Data COM ano e SEM "Hoje"/"Ontem" (correção C3): o rótulo relativo envelhece
// com a aba aberta atravessando a meia-noite, sem ninguém tocar na tela.
export function formatEventDay(iso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pt-BR', { ...DAY_FORMAT_OPTIONS, timeZone }).format(
    new Date(iso),
  );
}

export function formatEventTime(iso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('pt-BR', { ...TIME_FORMAT_OPTIONS, timeZone }).format(
    new Date(iso),
  );
}

export interface EventDayGroup {
  day: string;
  events: KnowledgeDocumentEvent[];
}

// Agrupa a lista JÁ CONCATENADA das páginas, na ordem em que veio: um dia
// partido entre duas páginas vira um grupo só. A ordem é a da rota (occurredAt
// decrescente, desempate por id), e não é refeita aqui.
export function groupEventsByDay(
  events: KnowledgeDocumentEvent[],
  timeZone?: string,
): EventDayGroup[] {
  const groups: EventDayGroup[] = [];
  for (const event of events) {
    const day = formatEventDay(event.occurredAt, timeZone);
    const last = groups[groups.length - 1];
    if (last && last.day === day) {
      last.events.push(event);
    } else {
      groups.push({ day, events: [event] });
    }
  }
  return groups;
}
