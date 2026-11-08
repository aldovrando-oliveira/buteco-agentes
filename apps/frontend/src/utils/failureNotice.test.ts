import { describe, expect, it } from 'vitest';
import { blockedUntil, failureNotice, failureText, isApiFailure } from './failureNotice';
import { ApiError as AccountApiError } from '../features/account/api/accountApi';
import { INICIANDO, MUITAS_TENTATIVAS, SEM_CONEXAO } from '../features/auth/messages';

// Outra classe com a forma do `ApiError`, como a cópia de cada feature (convenção 2).
class OtherApiError extends Error {
  readonly status: number;
  readonly title?: string;
  readonly retryAfter?: number;

  constructor(status: number, title?: string, retryAfter?: number) {
    super(title ?? `Erro ${status}`);
    this.status = status;
    this.title = title;
    this.retryAfter = retryAfter;
  }
}

describe('failureNotice', () => {
  it('o 423 com e sem Retry-After', () => {
    expect(failureText(new AccountApiError(423, 'x', { title: 'x' }, 300))).toBe(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
    );
    expect(failureText(new AccountApiError(423, 'x'))).toBe(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por alguns minutos.',
    );
    expect(failureNotice(new AccountApiError(423, 'x')).cor).toBe('yellow');
  });

  it('o 429, o 503 e o title', () => {
    expect(failureNotice(new AccountApiError(429, 'x'))).toEqual({ texto: MUITAS_TENTATIVAS, cor: 'yellow' });
    expect(failureNotice(new AccountApiError(503, 'x'))).toEqual({ texto: INICIANDO, cor: 'red' });
    expect(failureText(new AccountApiError(409, 'x', { title: 'Já existe.' }))).toBe('Já existe.');
  });

  it('o erro de rede', () => {
    expect(failureText(new TypeError('Failed to fetch'))).toBe(SEM_CONEXAO);
    expect(blockedUntil(new TypeError('Failed to fetch'))).toBeNull();
  });

  it('um erro de outra classe, com a forma de ApiError, recebe o texto do bloqueio', () => {
    const error = new OtherApiError(423, 'Acesso pausado.', 120);
    expect(isApiFailure(error)).toBe(true);
    expect(failureText(error)).toBe(
      'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 2 minutos.',
    );
    expect(blockedUntil(error, 1_000)).toBe(121_000);
  });

  it('um objeto sem status numérico não é ApiFailure', () => {
    expect(isApiFailure({ status: 423 })).toBe(false);
    expect(isApiFailure(Object.assign(new Error('x'), { status: '423' }))).toBe(false);
  });
});
