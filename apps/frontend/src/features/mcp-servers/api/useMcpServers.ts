import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateMcpServer,
  createMcpServer,
  deactivateMcpServer,
  getMcpServer,
  listMcpServers,
  listMcpServerTools,
  testSavedMcpServerConnection,
  testUnsavedMcpServerConnection,
  updateMcpServer,
} from './mcpServersApi';
import type {
  CreateMcpServerInput,
  McpServer,
  TestMcpServerConfigInput,
  UpdateMcpServerInput,
} from '../types/mcpServer';

// `enabled` opcional (mesmo formato de useMcpServerToolsQuery): o detalhe
// do agente só busca o catálogo quando a aba de ferramentas está ativa, e
// a maioria das visitas nunca a abre (Decision 5 do design.md da change
// frontend-agente-detalhe-abas).
export function useMcpServersQuery(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ['mcp-servers'],
    queryFn: listMcpServers,
    enabled: options?.enabled ?? true,
  });
}

export function useMcpServerQuery(id: string) {
  return useQuery({ queryKey: ['mcp-servers', id], queryFn: () => getMcpServer(id) });
}

export function useCreateMcpServerMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateMcpServerInput) => createMcpServer(input),
    onSuccess: (created: McpServer) => {
      queryClient.setQueryData(['mcp-servers', created.id], created);
      void queryClient.invalidateQueries({ queryKey: ['mcp-servers'] });
    },
  });
}

export function useUpdateMcpServerMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateMcpServerInput }) =>
      updateMcpServer(id, input),
    onSuccess: (updated: McpServer) => {
      queryClient.setQueryData(['mcp-servers', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['mcp-servers'] });
    },
  });
}

export function useActivateMcpServerMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => activateMcpServer(id),
    onSuccess: (updated: McpServer) => {
      queryClient.setQueryData(['mcp-servers', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['mcp-servers'] });
    },
  });
}

export function useDeactivateMcpServerMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => deactivateMcpServer(id),
    onSuccess: (updated: McpServer) => {
      queryClient.setQueryData(['mcp-servers', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['mcp-servers'] });
    },
  });
}

export function useTestUnsavedMcpServerConnectionMutation() {
  return useMutation({
    mutationFn: (input: TestMcpServerConfigInput) => testUnsavedMcpServerConnection(input),
  });
}

export function useTestSavedMcpServerConnectionMutation() {
  return useMutation({
    mutationFn: (id: string) => testSavedMcpServerConnection(id),
  });
}

export function useMcpServerToolsQuery(mcpServerId: string, options: { enabled: boolean }) {
  return useQuery({
    queryKey: ['mcp-servers', mcpServerId, 'tools'],
    queryFn: () => listMcpServerTools(mcpServerId),
    enabled: options.enabled,
    // Decision 3 do design.md: sem isso, reabrir um servidor já descoberto
    // dispararia um refetch em background (staleTime padrão é 0). O
    // resultado só deve mudar via retry manual (refetch()).
    staleTime: Infinity,
  });
}
