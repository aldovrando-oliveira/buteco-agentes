import { lockMinutes } from '../features/auth/entryNotices';
import { INICIANDO, MUITAS_TENTATIVAS, SEM_CONEXAO } from '../features/auth/messages';

// Os avisos dos cartões para o bloqueio e as falhas que o quadro não desenha (design.md da
// change minha-conta-painel, D8; decisão do dono Q2): os textos do servidor ou os do
// /entrar, e o botão da ação desabilitado até o `Retry-After`.
//
// Comum a Minha conta e a Usuários (design.md da change usuarios-convites-identidade-painel,
// D1; decisão do dono Q2). Cada feature tem a sua cópia do `request<T>` e do `ApiError`
// (convenção 2), então o erro é reconhecido pelos campos, e não pela classe.

/** Os campos do `ApiError` de qualquer feature que este módulo lê. */
export interface ApiFailure {
  status: number;
  retryAfter?: number;
  title?: string;
}

/** Um erro com a forma do `ApiError` das features: `status` numérico, e os outros, se vierem, do tipo certo. */
export function isApiFailure(error: unknown): error is ApiFailure {
  if (!(error instanceof Error)) return false;
  const e = error as Error & { status?: unknown; retryAfter?: unknown; title?: unknown };
  return (
    typeof e.status === 'number' &&
    (e.retryAfter === undefined || typeof e.retryAfter === 'number') &&
    (e.title === undefined || typeof e.title === 'string')
  );
}

const GENERICO = 'Não foi possível concluir agora. Tente de novo.';

/** O texto do bloqueio do quadro, com o N do `Retry-After` (o quadro diz "5 minutos" fixo; #217). */
export function lockedText(retryAfter: number | undefined): string {
  const minutos = lockMinutes(retryAfter);
  const tempo = minutos === null ? 'alguns minutos' : `${minutos} ${minutos === 1 ? 'minuto' : 'minutos'}`;
  return `Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por ${tempo}.`;
}

/** O aviso de uma falha que não vai em campo: bloqueio, `429`, `503`, rede, ou o `title`. */
export function failureText(error: unknown): string {
  if (!isApiFailure(error)) return SEM_CONEXAO;
  switch (error.status) {
    case 423:
      return lockedText(error.retryAfter);
    case 429:
      return MUITAS_TENTATIVAS;
    case 503:
      return INICIANDO;
    default:
      return error.title ?? GENERICO;
  }
}

export interface FailureNotice {
  texto: string;
  /** Amarelo para o bloqueio e o excesso de tentativas, como no quadro; vermelho para o resto. */
  cor: 'yellow' | 'red';
}

export function failureNotice(error: unknown): FailureNotice {
  const amarelo = isApiFailure(error) && (error.status === 423 || error.status === 429);
  return { texto: failureText(error), cor: amarelo ? 'yellow' : 'red' };
}

/** Até quando o botão da ação fica desabilitado: o `Retry-After` do `423`, do `429` e do `503`. */
export function blockedUntil(error: unknown, now = Date.now()): number | null {
  if (!isApiFailure(error)) return null;
  if (error.status !== 423 && error.status !== 429 && error.status !== 503) return null;
  if (error.retryAfter === undefined) return error.status === 423 ? null : now + 1000;
  return now + error.retryAfter * 1000;
}
