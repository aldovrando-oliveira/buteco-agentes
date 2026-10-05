import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateKnowledgeBase,
  createKnowledgeBase,
  deactivateKnowledgeBase,
  getKnowledgeBase,
  listKnowledgeBaseIndexingSummary,
  listKnowledgeBases,
  updateKnowledgeBase,
} from './knowledgeBasesApi';
import type {
  CreateKnowledgeBaseInput,
  KnowledgeBase,
  UpdateKnowledgeBaseInput,
} from '../types/knowledgeBase';
import { syncWaitInterval, type SyncRequest } from '../utils/syncState';

// `enabled` opcional, no mesmo formato de useMcpServersQuery: a etapa do
// vínculo (5b) vai buscar o catálogo só quando a aba de conhecimento do agente
// estiver ativa.
export function useKnowledgeBasesQuery(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['knowledge-bases'],
    queryFn: listKnowledgeBases,
    enabled: options?.enabled ?? true,
  });
}

// CHAVE ESCOLHIDA, NÃO SORTEADA: o prefixo `['knowledge-bases']` faz toda
// mutação de base invalidar este resumo de graça — criar ou editar uma base muda
// o conjunto de linhas que ele devolve, e `cacheUpdatedKnowledgeBase` abaixo já
// invalida por esse prefixo.
//
// SEM `refetchInterval`, e a recusa foi conferida antes de escrita (design.md,
// D8). O polling condicional que a etapa 5a-2 introduziu na tabela de documentos
// acompanha uma transição que o operador acabou de provocar, NA TELA em que a
// provocou; o catálogo é tela de navegação, e ninguém carrega documento por ele.
//
// E a frescura já está resolvida sem polling: `app/queryClient.ts` cria
// `new QueryClient()` SEM `defaultOptions`, então valem `staleTime: 0` e
// `refetchOnMount: true` — quem sobe um documento no detalhe e volta ao catálogo
// remonta a página, e esta consulta é refeita. Por isso também não se acrescenta
// invalidação deste resumo às mutações de DOCUMENTO: seria código para um
// problema que os defaults já resolvem.
//
// Gatilho para o polling nascer aqui, nomeado para não virar adiamento
// indefinido: uma tela de operação que espere transição NO CATÁLOGO. Hoje não
// existe.
export const indexingSummaryQueryKey = ['knowledge-bases', 'indexing-summary'] as const;

export function useKnowledgeBaseIndexingSummaryQuery() {
  return useQuery({
    queryKey: indexingSummaryQueryKey,
    queryFn: listKnowledgeBaseIndexingSummary,
  });
}

// `syncRequest` liga a consulta periódica CONDICIONAL do "Sincronizar agora"
// (frontend-detalhe-base-sincronizada, D5; convenção 20): a cada 4 s enquanto
// `syncState.lastFinishedAt` for igual à linha de base do pedido e o pedido tiver
// menos de 5 minutos, `false` em qualquer outro caso. A condição é a função pura
// `syncWaitInterval`; sem pedido, a consulta é a de sempre.
export function useKnowledgeBaseQuery(id: string, options?: { syncRequest?: SyncRequest | null }) {
  const syncRequest = options?.syncRequest ?? null;

  return useQuery({
    queryKey: ['knowledge-bases', id],
    queryFn: () => getKnowledgeBase(id),
    refetchInterval: (query) =>
      syncWaitInterval(syncRequest, query.state.data as KnowledgeBase | undefined, Date.now()),
  });
}

// Toda mutação escreve o item no cache e invalida a coleção: a resposta já traz
// a base atualizada, então o detalhe não precisa de um segundo GET, e a
// listagem precisa refazer a consulta porque a ordem e o filtro dependem dela.
function cacheUpdatedKnowledgeBase(
  queryClient: ReturnType<typeof useQueryClient>,
  updated: KnowledgeBase,
) {
  queryClient.setQueryData(['knowledge-bases', updated.id], updated);
  void queryClient.invalidateQueries({ queryKey: ['knowledge-bases'] });
}

export function useCreateKnowledgeBaseMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateKnowledgeBaseInput) => createKnowledgeBase(input),
    onSuccess: (created: KnowledgeBase) => cacheUpdatedKnowledgeBase(queryClient, created),
  });
}

export function useUpdateKnowledgeBaseMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateKnowledgeBaseInput }) =>
      updateKnowledgeBase(id, input),
    onSuccess: (updated: KnowledgeBase) => cacheUpdatedKnowledgeBase(queryClient, updated),
  });
}

export function useActivateKnowledgeBaseMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => activateKnowledgeBase(id),
    onSuccess: (updated: KnowledgeBase) => cacheUpdatedKnowledgeBase(queryClient, updated),
  });
}

export function useDeactivateKnowledgeBaseMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => deactivateKnowledgeBase(id),
    onSuccess: (updated: KnowledgeBase) => cacheUpdatedKnowledgeBase(queryClient, updated),
  });
}
