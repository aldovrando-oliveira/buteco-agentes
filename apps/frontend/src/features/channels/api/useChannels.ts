import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  activateChannel,
  createChannel,
  deactivateChannel,
  getChannel,
  listChannels,
  updateChannel,
} from './channelsApi';
import type { Channel, CreateChannelInput, UpdateChannelInput } from '../types/channel';

export function useChannelsQuery() {
  return useQuery({ queryKey: ['channels'], queryFn: listChannels });
}

export function useChannelQuery(id: string) {
  return useQuery({ queryKey: ['channels', id], queryFn: () => getChannel(id) });
}

export function useCreateChannelMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateChannelInput) => createChannel(input),
    onSuccess: (created: Channel) => {
      queryClient.setQueryData(['channels', created.id], created);
      void queryClient.invalidateQueries({ queryKey: ['channels'] });
    },
  });
}

export function useUpdateChannelMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateChannelInput }) =>
      updateChannel(id, input),
    onSuccess: (updated: Channel) => {
      queryClient.setQueryData(['channels', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['channels'] });
    },
  });
}

export function useActivateChannelMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => activateChannel(id),
    onSuccess: (updated: Channel) => {
      queryClient.setQueryData(['channels', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['channels'] });
    },
  });
}

export function useDeactivateChannelMutation() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => deactivateChannel(id),
    onSuccess: (updated: Channel) => {
      queryClient.setQueryData(['channels', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['channels'] });
    },
  });
}
