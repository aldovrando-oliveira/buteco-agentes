import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createAgent, getAgent, listAgents } from './agentsApi';
import type { Agent, CreateAgentInput } from '../types/agent';

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
