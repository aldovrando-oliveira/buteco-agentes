import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateKnowledgeBase,
  createKnowledgeBase,
  deactivateKnowledgeBase,
  getKnowledgeBase,
  listKnowledgeBases,
  updateKnowledgeBase,
} from './knowledgeBasesApi';
import type {
  CreateKnowledgeBaseInput,
  KnowledgeBase,
  UpdateKnowledgeBaseInput,
} from '../types/knowledgeBase';

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

export function useKnowledgeBaseQuery(id: string) {
  return useQuery({ queryKey: ['knowledge-bases', id], queryFn: () => getKnowledgeBase(id) });
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
