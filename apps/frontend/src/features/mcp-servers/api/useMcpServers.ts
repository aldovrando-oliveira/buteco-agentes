import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateMcpServer,
  createMcpServer,
  deactivateMcpServer,
  getMcpServer,
  listMcpServers,
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

export function useMcpServersQuery() {
  return useQuery({ queryKey: ['mcp-servers'], queryFn: listMcpServers });
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
