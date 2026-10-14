import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type InfiniteData,
  type QueryClient,
} from '@tanstack/react-query';
import {
  createKnowledgeDocument,
  deleteKnowledgeDocument,
  getKnowledgeDocument,
  listKnowledgeDocumentEvents,
  listKnowledgeDocuments,
  reindexKnowledgeDocument,
  updateKnowledgeDocument,
} from './knowledgeDocumentsApi';
import { hasNonTerminalDocument } from '../utils/documentIndexing';
import type { KnowledgeDocumentEventPage } from '../types/knowledgeDocumentEvent';
import type {
  CreateKnowledgeDocumentInput,
  KnowledgeDocumentSummary,
  UpdateKnowledgeDocumentInput,
} from '../types/knowledgeDocument';

export const documentsQueryKey = (knowledgeBaseId: string) =>
  ['knowledge-bases', knowledgeBaseId, 'documents'] as const;

// Intervalo do acompanhamento. Abaixo dos 5 s da timeline de sessões de
// propósito: lá a espera é por uma resposta de agente, que leva segundos; aqui é
// por um passo de pipeline que a 2a mede em centenas de milissegundos mais a
// fila.
const POLL_INTERVAL_MS = 4000;

// PRIMEIRO POLLING CONDICIONAL DO PAINEL, e o padrão nasce aqui.
//
// O painel já fazia polling — useSessionMessagesQuery usa
// `refetchInterval: 5000` —, mas com intervalo CONSTANTE: a condição lá é "a
// timeline está aberta", não "os dados ainda mudam". Esta é a primeira consulta
// cujo intervalo depende do CONTEÚDO da resposta.
//
// A forma de função é suportada pela tipagem instalada:
// `number | false | ((query) => number | false | undefined)`
// (@tanstack/query-core, _tsup-dts-rollup.d.ts:1675). Conferido, não deduzido.
//
// A condição mora numa função pura exportada, e não numa expressão embutida
// aqui, por dois motivos: é testável sem timer, e a faixa de resumo da tela faz
// a mesma pergunta sobre a mesma lista.
//
// `refetchIntervalInBackground` fica no default (false) — painel em aba de fundo
// não precisa acompanhar.
//
// SEM barra de progresso percentual em lugar nenhum: o sistema conhece o ESTADO
// do documento e não conhece o percentual (convenção 13).
export function useKnowledgeDocumentsQuery(knowledgeBaseId: string) {
  return useQuery({
    queryKey: documentsQueryKey(knowledgeBaseId),
    queryFn: () => listKnowledgeDocuments(knowledgeBaseId),
    refetchInterval: (query) =>
      hasNonTerminalDocument(query.state.data as KnowledgeDocumentSummary[] | undefined)
        ? POLL_INTERVAL_MS
        : false,
  });
}

// Busca o documento inteiro só quando o modal de atualizar está aberto — é a
// única tela que precisa de `extractedText`, e a listagem não o traz.
export function useKnowledgeDocumentQuery(knowledgeBaseId: string, id: string | null) {
  return useQuery({
    queryKey: ['knowledge-bases', knowledgeBaseId, 'documents', id],
    queryFn: () => getKnowledgeDocument(knowledgeBaseId, id!),
    enabled: id !== null,
  });
}

// Toda mutação invalida a listagem: o estado de indexação muda do lado do
// servidor e a lista é a fonte da tela. Não há setQueryData do item aqui porque
// a listagem é de summaries e as mutações devolvem o documento completo — dois
// formatos diferentes.
function invalidateDocuments(
  queryClient: ReturnType<typeof useQueryClient>,
  knowledgeBaseId: string,
) {
  void queryClient.invalidateQueries({ queryKey: documentsQueryKey(knowledgeBaseId) });
}

export function useCreateKnowledgeDocumentMutation(knowledgeBaseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (input: CreateKnowledgeDocumentInput) =>
      createKnowledgeDocument(knowledgeBaseId, input),
    onSuccess: () => invalidateDocuments(queryClient, knowledgeBaseId),
  });
}

export function useUpdateKnowledgeDocumentMutation(knowledgeBaseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateKnowledgeDocumentInput }) =>
      updateKnowledgeDocument(knowledgeBaseId, id, input),
    onSuccess: () => invalidateDocuments(queryClient, knowledgeBaseId),
  });
}

export function useDeleteKnowledgeDocumentMutation(knowledgeBaseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => deleteKnowledgeDocument(knowledgeBaseId, id),
    onSuccess: () => invalidateDocuments(queryClient, knowledgeBaseId),
  });
}

export function useReindexKnowledgeDocumentMutation(knowledgeBaseId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => reindexKnowledgeDocument(knowledgeBaseId, id),
    onSuccess: () => invalidateDocuments(queryClient, knowledgeBaseId),
  });
}

// HISTÓRICO DE DOCUMENTOS (design.md da change frontend-aba-historico, D6).
//
// CHAVE FORA DO PREFIXO DAS BASES, e isso é escolha, pelo mesmo motivo da
// chave do diagnóstico (useKnowledgeIndex.ts): `cacheUpdatedKnowledgeBase`
// (useKnowledgeBases.ts) invalida `['knowledge-bases']` SEM `exact` a cada
// criar, editar, ativar e desativar, e uma consulta infinita alcançada por essa
// invalidação é relida INTEIRA, página por página. Mudar a base não muda o
// histórico: nenhuma dessas escritas gera evento de documento (#98, D2).
export const documentEventsQueryKey = (knowledgeBaseId: string) =>
  ['knowledge-document-events', knowledgeBaseId] as const;

// Primeira paginação do painel. `nextCursor` nulo vira `undefined`, que é o que
// faz `hasNextPage` falso (query-core 5.101.4, infiniteQueryBehavior.js:113-116).
//
// SEM `refetchInterval`: o histórico não é processo em andamento (convenção 20).
// E SEM releitura no foco: o refetch de uma consulta infinita relê TODAS as
// páginas carregadas (infiniteQueryBehavior.js:70-78); eventos novos aparecem
// pelo `Atualizar` e pela volta à aba, que recomeçam da primeira (abaixo).
//
// `enabled` vem da página: só com a aba Histórico ativa.
export function useKnowledgeDocumentEventsQuery(
  knowledgeBaseId: string,
  options: { enabled: boolean },
) {
  return useInfiniteQuery({
    queryKey: documentEventsQueryKey(knowledgeBaseId),
    queryFn: ({ pageParam }) => listKnowledgeDocumentEvents(knowledgeBaseId, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (page: KnowledgeDocumentEventPage) => page.nextCursor ?? undefined,
    enabled: options.enabled,
    refetchOnWindowFocus: false,
  });
}

// APARAR PARA A PRIMEIRA PÁGINA, SEM RELER. É o que a saída da aba faz: a volta
// habilita o observador, e a busca que isso dispara é de uma página só
// (queryObserver.js:457-459), porque só há uma no cache.
//
// Cancela SÓ um "Carregar mais" em voo: se ele terminasse depois do corte,
// gravaria as duas páginas de novo. Cancelado, ele reverte o estado para o de
// antes dele (query.js, `#revertState`), e o corte vem depois. A busca da
// primeira página NÃO é cancelada: com o painel em StrictMode, a limpeza do
// efeito de saída roda logo após a montagem, e o cancelamento incondicional da
// primeira versão fazia a entrada na aba pedir duas vezes (conferência visual,
// R1-1; medido pela guarda 7.17, que volta a cancelar sempre e reprova o teste
// em StrictMode).
export async function trimDocumentEvents(queryClient: QueryClient, knowledgeBaseId: string) {
  const queryKey = documentEventsQueryKey(knowledgeBaseId);
  const state = queryClient.getQueryState(queryKey);
  if (state?.fetchStatus === 'fetching' && state.fetchMeta?.fetchMore) {
    await queryClient.cancelQueries({ queryKey, exact: true });
  }
  queryClient.setQueryData<InfiniteData<KnowledgeDocumentEventPage, string | null>>(
    queryKey,
    (data) => data && { pages: data.pages.slice(0, 1), pageParams: data.pageParams.slice(0, 1) },
  );
}

// OS RECOMEÇOS COM A ABA ABERTA (fim do "Sincronizar agora", `Atualizar`): apara
// e relê só a primeira. Aparada, a releitura é de UMA requisição sem cursor,
// porque o refetch relê `pages.length` páginas. `resetQueries` não serve: ele
// apaga `data` antes de reler (queryClient.js:126-139), e o `Atualizar` não pode
// devolver a tela ao carregamento. `type: 'active'` só relê com o observador
// habilitado (query.js:97-101): com a aba fechada, equivale a aparar.
export async function restartDocumentEvents(queryClient: QueryClient, knowledgeBaseId: string) {
  const queryKey = documentEventsQueryKey(knowledgeBaseId);
  await trimDocumentEvents(queryClient, knowledgeBaseId);
  await queryClient.refetchQueries({ queryKey, type: 'active', exact: true });
}
