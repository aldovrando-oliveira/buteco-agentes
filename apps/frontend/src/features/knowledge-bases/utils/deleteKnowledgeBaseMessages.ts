import { ApiError } from '../api/knowledgeBasesApi';

// TEXTO DA TELA PARA A RESPOSTA DE `DELETE /knowledge-bases/{id}` — a tabela da D3
// do design.md da change exclusao-base-painel. O texto vem do status e do `code`,
// nunca do `title` nem do `detail` do ProblemDetails.
//
// O contrato da rota é da #108 (KnowledgeBaseEndpoints.cs, DeleteKnowledgeBaseAsync):
// 204, 409 `knowledge-base-active`, 404 e 503 com Retry-After. O Retry-After não
// é lido: o CORS do apps/api não o expõe (Program.cs, AddCors sem
// WithExposedHeaders), e o valor é fixo em 1 segundo — "em instantes" diz o mesmo.
// Gatilho: a rota passar a devolver outro status ou o 503 passar a variar a espera.

export type DeleteKnowledgeBaseFailureKind =
  'active' | 'not-found' | 'contended' | 'unexpected' | 'network';

export interface DeleteKnowledgeBaseFailure {
  kind: DeleteKnowledgeBaseFailureKind;
  message: string;
  // Se a confirmação continua oferecendo "Excluir base". Só o 503 diz que repetir
  // resolve; nos outros, repetir recebe a mesma resposta ou o estado é incerto.
  canRetry: boolean;
}

export function deleteKnowledgeBaseFailure(error: unknown): DeleteKnowledgeBaseFailure {
  // Sem ApiError, a requisição não teve resposta: a exclusão pode ter acontecido
  // ou não, e o texto não afirma nenhum dos dois (convenção 13).
  if (!(error instanceof ApiError)) {
    return {
      kind: 'network',
      message:
        'Não foi possível confirmar a exclusão: o servidor não respondeu. Recarregue a página para conferir se a base ainda existe.',
      canRetry: false,
    };
  }

  if (error.status === 409 && error.problem?.code === 'knowledge-base-active') {
    return {
      kind: 'active',
      message:
        'A base foi ativada depois que esta tela abriu, e só uma base inativa pode ser excluída. Nada foi excluído.',
      canRetry: false,
    };
  }

  if (error.status === 404) {
    return {
      kind: 'not-found',
      message:
        'Esta base já tinha sido excluída, provavelmente em outra aba. Nada mais foi excluído.',
      canRetry: false,
    };
  }

  if (error.status === 503) {
    return {
      kind: 'contended',
      message:
        'Outra operação nesta base impediu a exclusão. Nada foi excluído; tente de novo em instantes.',
      canRetry: true,
    };
  }

  return {
    kind: 'unexpected',
    message: `A exclusão não foi concluída (status ${error.status}). Recarregue a página para conferir o estado da base.`,
    canRetry: false,
  };
}
