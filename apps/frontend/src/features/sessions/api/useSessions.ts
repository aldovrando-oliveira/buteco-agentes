import { useQuery } from '@tanstack/react-query';
import { getSessionMessages, listChannelSessions } from './sessionsApi';

// Sem refetchInterval — o refetch natural do react-query (foco de janela,
// remount) já cobre a lista (design.md, Decisão 4).
export function useChannelSessionsQuery(channelId: string) {
  return useQuery({
    queryKey: ['sessions', 'byChannel', channelId],
    queryFn: () => listChannelSessions(channelId),
  });
}

// refetchInterval: 5000 só enquanto a timeline está aberta (sessionId
// presente); acima do sweep de 2s do backend (design.md, Decisão 4).
// refetchIntervalInBackground fica no default (false).
export function useSessionMessagesQuery(sessionId: string | undefined) {
  return useQuery({
    queryKey: ['sessions', sessionId, 'messages'],
    queryFn: () => getSessionMessages(sessionId!),
    enabled: Boolean(sessionId),
    refetchInterval: 5000,
  });
}
