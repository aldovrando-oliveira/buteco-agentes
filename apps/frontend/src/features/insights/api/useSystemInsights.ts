import { useQuery } from '@tanstack/react-query';
import { getSystemInsights } from './insightsApi';
import { insightsWindow, type InsightsPeriod } from '../utils/insightsWindow';

// A CHAVE É O PERÍODO, E A JANELA É CALCULADA DENTRO DO queryFn.
//
// Se a janela fosse calculada no render e entrasse na chave, cada render geraria
// uma chave nova — o relógio andou —, cada chave nova dispararia uma consulta
// nova, e a página ficaria presa em carregamento para sempre. É exatamente o
// modo de falha que `features/sessions/api/useSessions.ts` documenta, e a
// solução aqui é a mesma: a chave carrega o PERÍODO (que só muda quando o
// operador troca), e a janela nasce no instante da consulta.
//
// É isso que faz nova tentativa, refetch por foco e remount consultarem a janela
// ATUALIZADA — que é o que "últimos 30 dias" quer dizer.
//
// Sem refetchInterval: a página de Insights não é painel ao vivo.
export function useSystemInsightsQuery(period: InsightsPeriod) {
  return useQuery({
    queryKey: ['insights', 'system', period],
    queryFn: () => {
      const { from, to } = insightsWindow(period, new Date());
      return getSystemInsights(from, to);
    },
  });
}
