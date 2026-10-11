import { describe, expect, it } from 'vitest';
import { ApiError } from '../api/knowledgeBasesApi';
import { deleteKnowledgeBaseFailure } from './deleteKnowledgeBaseMessages';

const SERVER_TITLE = 'TITULO-DO-SERVIDOR';
const SERVER_DETAIL = 'DETALHE-DO-SERVIDOR';

function apiError(status: number, code?: string): ApiError {
  return new ApiError(status, SERVER_TITLE, {
    title: SERVER_TITLE,
    status,
    detail: SERVER_DETAIL,
    ...(code ? { code } : {}),
  });
}

// A tabela da D3 do design.md da change exclusao-base-painel.
describe('deleteKnowledgeBaseFailure', () => {
  it('409 knowledge-base-active: a base foi ativada, nada foi excluído, sem repetir', () => {
    const failure = deleteKnowledgeBaseFailure(apiError(409, 'knowledge-base-active'));

    expect(failure.kind).toBe('active');
    expect(failure.message).toBe(
      'A base foi ativada depois que esta tela abriu, e só uma base inativa pode ser excluída. Nada foi excluído.',
    );
    expect(failure.canRetry).toBe(false);
  });

  it('404: já tinha sido excluída, nada mais foi excluído, sem repetir', () => {
    const failure = deleteKnowledgeBaseFailure(apiError(404));

    expect(failure.kind).toBe('not-found');
    expect(failure.message).toBe(
      'Esta base já tinha sido excluída, provavelmente em outra aba. Nada mais foi excluído.',
    );
    expect(failure.canRetry).toBe(false);
  });

  it('503: outra operação impediu, nada foi excluído, pode repetir', () => {
    const failure = deleteKnowledgeBaseFailure(apiError(503));

    expect(failure.kind).toBe('contended');
    expect(failure.message).toBe(
      'Outra operação nesta base impediu a exclusão. Nada foi excluído; tente de novo em instantes.',
    );
    expect(failure.canRetry).toBe(true);
  });

  it('409 com code desconhecido cai no texto de outro status, não no de base ativa', () => {
    const failure = deleteKnowledgeBaseFailure(apiError(409, 'codigo-novo'));

    expect(failure.kind).toBe('unexpected');
    expect(failure.message).toBe(
      'A exclusão não foi concluída (status 409). Recarregue a página para conferir o estado da base.',
    );
    expect(failure.message).not.toMatch(/ativada/);
    expect(failure.canRetry).toBe(false);
  });

  it('outro status diz o status e manda conferir, sem repetir', () => {
    const failure = deleteKnowledgeBaseFailure(apiError(500));

    expect(failure.kind).toBe('unexpected');
    expect(failure.message).toContain('(status 500)');
    expect(failure.canRetry).toBe(false);
  });

  it('falha de rede não afirma se excluiu ou não', () => {
    const failure = deleteKnowledgeBaseFailure(new TypeError('Failed to fetch'));

    expect(failure.kind).toBe('network');
    expect(failure.message).toBe(
      'Não foi possível confirmar a exclusão: o servidor não respondeu. Recarregue a página para conferir se a base ainda existe.',
    );
    expect(failure.message).not.toMatch(/Nada foi excluído/);
    expect(failure.canRetry).toBe(false);
  });

  it('nenhum texto repete o title nem o detail do servidor', () => {
    for (const error of [
      apiError(409, 'knowledge-base-active'),
      apiError(409, 'codigo-novo'),
      apiError(404),
      apiError(503),
      apiError(500),
    ]) {
      const { message } = deleteKnowledgeBaseFailure(error);
      expect(message).not.toContain(SERVER_TITLE);
      expect(message).not.toContain(SERVER_DETAIL);
    }
  });
});
