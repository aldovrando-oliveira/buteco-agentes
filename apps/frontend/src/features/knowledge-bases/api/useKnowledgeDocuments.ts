import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  createKnowledgeDocument,
  deleteKnowledgeDocument,
  getKnowledgeDocument,
  listKnowledgeDocuments,
  reindexKnowledgeDocument,
  updateKnowledgeDocument,
} from './knowledgeDocumentsApi';
import { hasNonTerminalDocument } from '../utils/documentIndexing';
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
