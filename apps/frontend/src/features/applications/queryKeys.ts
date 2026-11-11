import type { ListFilter } from './api/applicationsApi';

// As chaves das consultas de aplicações externas. NENHUMA delas guarda resposta com segredo: o
// `valor` só existe nas respostas de criação, que não passam por consulta (D3).

export const APPLICATIONS_KEY = ['external-applications'] as const;

export const applicationsListKey = (filter: ListFilter) =>
  [
    ...APPLICATIONS_KEY,
    'list',
    { busca: filter.busca?.trim() ?? '', estado: filter.estado ?? 'todas' },
  ] as const;

export const applicationKey = (id: string) => [...APPLICATIONS_KEY, 'detail', id] as const;

export const applicationAgentsKey = (id: string) =>
  [...APPLICATIONS_KEY, 'detail', id, 'agents'] as const;

/** O card do agente: a lista (Admin) ou a contagem (Operador e Leitor), cada uma com a sua chave. */
export const agentApplicationsKey = (agentId: string, modo: 'lista' | 'contagem') =>
  [...APPLICATIONS_KEY, 'agent', agentId, modo] as const;
