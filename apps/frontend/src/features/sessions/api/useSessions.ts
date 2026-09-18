import { useQuery } from '@tanstack/react-query';
import {
  getMessagesSummary,
  getSessionMessages,
  getSessionsSummary,
  listChannelSessions,
} from './sessionsApi';
import { lastSevenDaysWindow } from '../../inventory/utils/activityWindow';

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

// AS DUAS CHAVES ABAIXO SÃO FIXAS, E A JANELA É CALCULADA DENTRO DO queryFn.
//
// Se a janela fosse calculada no render e entrasse na chave, cada render geraria
// uma chave nova — o relógio andou —, cada chave nova dispararia uma consulta
// nova, e o item do inventário ficaria preso em "Consultando…" para sempre. Com
// a chave fixa, nova tentativa, refetch por foco e remount recalculam a janela no
// INSTANTE DA CONSULTA, que é o que "últimos 7 dias" quer dizer
// (frontend-inventario-atividade-periodo, design.md, D3).
//
// Sem refetchInterval: a tela de entrada não é painel ao vivo.
export function useSessionsSummaryQuery() {
  return useQuery({
    queryKey: ['sessions', 'summary', 'last-7-days'],
    queryFn: () => {
      const { from, to } = lastSevenDaysWindow(new Date());
      return getSessionsSummary(from, to);
    },
  });
}

export function useMessagesSummaryQuery() {
  return useQuery({
    queryKey: ['messages', 'summary', 'last-7-days'],
    queryFn: () => {
      const { from, to } = lastSevenDaysWindow(new Date());
      return getMessagesSummary(from, to);
    },
  });
}
