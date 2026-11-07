import { ApiError } from './api/accountApi';
import { lockMinutes } from '../auth/entryNotices';
import { INICIANDO, MUITAS_TENTATIVAS, SEM_CONEXAO } from '../auth/messages';

// Os avisos dos cartões para o bloqueio e as falhas que o quadro não desenha (design.md da
// change minha-conta-painel, D8; decisão do dono Q2): os textos do servidor ou os do
// /entrar, e o botão da ação desabilitado até o `Retry-After`.

const GENERICO = 'Não foi possível concluir agora. Tente de novo.';

/** O texto do bloqueio do quadro, com o N do `Retry-After` (o quadro diz "5 minutos" fixo; #217). */
export function lockedText(retryAfter: number | undefined): string {
  const minutos = lockMinutes(retryAfter);
  const tempo = minutos === null ? 'alguns minutos' : `${minutos} ${minutos === 1 ? 'minuto' : 'minutos'}`;
  return `Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por ${tempo}.`;
}

/** O aviso de uma falha que não vai em campo: bloqueio, `429`, `503`, rede, ou o `title`. */
export function failureText(error: unknown): string {
  if (!(error instanceof ApiError)) return SEM_CONEXAO;
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
  const amarelo = error instanceof ApiError && (error.status === 423 || error.status === 429);
  return { texto: failureText(error), cor: amarelo ? 'yellow' : 'red' };
}

/** Até quando o botão da ação fica desabilitado: o `Retry-After` do `423`, do `429` e do `503`. */
export function blockedUntil(error: unknown, now = Date.now()): number | null {
  if (!(error instanceof ApiError)) return null;
  if (error.status !== 423 && error.status !== 429 && error.status !== 503) return null;
  if (error.retryAfter === undefined) return error.status === 423 ? null : now + 1000;
  return now + error.retryAfter * 1000;
}
