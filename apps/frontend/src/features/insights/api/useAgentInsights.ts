import { useQuery } from '@tanstack/react-query';
import { getAgentInsights } from './insightsApi';
import { insightsWindow, type InsightsPeriod } from '../utils/insightsWindow';

// A CHAVE CARREGA O AGENTE E O PERÍODO; A JANELA NASCE DENTRO DO `queryFn`.
//
// Se a janela fosse calculada no render e entrasse na chave, cada render geraria
// uma chave nova — o relógio andou —, cada chave nova dispararia uma consulta
// nova, e a aba ficaria presa em carregamento para sempre. É o modo de falha que
// `features/sessions/api/useSessions.ts` documenta, e que
// `useSystemInsights.ts` já evita pelo mesmo caminho: a chave carrega o que só
// muda por escolha do operador, e a janela nasce no instante da consulta.
//
// É isso que faz nova tentativa, refetch por foco e remount consultarem a janela
// ATUALIZADA — que é o que "últimos 30 dias" quer dizer.
//
// O `id` ENTRA NA CHAVE, e não é detalhe: sem ele, abrir a aba de um agente e
// depois a de outro serviria o agregado do primeiro do cache. A chave do escopo
// do sistema não tem esse eixo, e é a única diferença estrutural entre os dois
// hooks.
//
// SEM `enabled`, E ISSO É DECISÃO (D12). As abas de ferramentas e conhecimento
// precisam do `enabled` porque a PÁGINA hospeda aquelas consultas e existe o
// tempo todo. Esta consulta vive dentro do componente da aba, e o
// `<Tabs keepMounted={false}>` da página garante que ele só existe no DOM quando
// a aba está ativa — o carregamento tardio sai do desmonte, não de uma bandeira.
// O guarda de `AgentDetailPage.test.tsx` afirma isso, porque é comportamento que
// se perde em silêncio numa mudança de `keepMounted`.
//
// Sem `refetchInterval`: a aba de Insights não é painel ao vivo.
export function useAgentInsightsQuery(agentId: string, period: InsightsPeriod) {
  return useQuery({
    queryKey: ['insights', 'agent', agentId, period],
    queryFn: () => {
      const { from, to } = insightsWindow(period, new Date());
      return getAgentInsights(agentId, from, to);
    },
    // O 404 é uma RESPOSTA, não uma falha de comunicação: repetir a pergunta
    // não muda a resposta. Sem isto, o react-query tentaria de novo três vezes
    // antes de a aba poder apresentar o estado de recusa (D17).
    //
    // O `3` NÃO É NÚMERO SOLTO: é o default da biblioteca, e
    // `src/app/queryClient.ts` monta o cliente SEM `defaultOptions`, então é o
    // que todas as outras consultas do painel já usam. Repeti-lo aqui mantém o
    // comportamento igual ao do resto da casa para tudo que não é `404`.
    //
    // CONSEQUÊNCIA QUE CUSTA UM TESTE: política declarada na consulta vence a
    // do cliente, então um `QueryClient` com `retry: false` NÃO desliga esta.
    // Teste que precise ver o estado de erro sem retentativa tem de rejeitar de
    // forma persistente, e não com `mockRejectedValueOnce`.
    retry: (failureCount, error) =>
      (error as { status?: number }).status === 404 ? false : failureCount < 3,
  });
}
