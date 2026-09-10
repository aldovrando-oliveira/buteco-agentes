import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateAgent,
  createAgent,
  deactivateAgent,
  getAgent,
  listAgents,
  replaceAgentDelegations,
  replaceAgentKnowledgeBases,
  replaceAgentMcpServers,
  updateAgent,
} from './agentsApi';
import type {
  Agent,
  AgentMcpServerBinding,
  CreateAgentInput,
  UpdateAgentInput,
} from '../types/agent';

export function useAgentsQuery() {
  return useQuery({ queryKey: ['agents'], queryFn: listAgents });
}

export function useAgentQuery(id: string) {
  return useQuery({ queryKey: ['agents', id], queryFn: () => getAgent(id) });
}

export function useCreateAgentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateAgentInput) => createAgent(input),
    onSuccess: (created: Agent) => {
      queryClient.setQueryData(['agents', created.id], created);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

export function useUpdateAgentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateAgentInput }) => updateAgent(id, input),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

export function useActivateAgentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => activateAgent(id),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

export function useDeactivateAgentMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => deactivateAgent(id),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

export function useReplaceAgentMcpServersMutation(agentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (bindings: AgentMcpServerBinding[]) => replaceAgentMcpServers(agentId, bindings),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

export function useReplaceAgentDelegationsMutation(agentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (targetAgentIds: string[]) => replaceAgentDelegations(agentId, targetAgentIds),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}

// Mesmo molde das outras duas mutações de vínculo: a resposta já traz o agente
// inteiro atualizado, então o detalhe não precisa de um segundo GET, e a
// listagem é invalidada porque `knowledgeBases` aparece em cada agente dela.
export function useReplaceAgentKnowledgeBasesMutation(agentId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (knowledgeBaseIds: string[]) =>
      replaceAgentKnowledgeBases(agentId, knowledgeBaseIds),
    onSuccess: (updated: Agent) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}
