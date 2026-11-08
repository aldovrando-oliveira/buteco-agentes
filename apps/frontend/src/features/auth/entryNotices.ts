import type { HttpFailure } from './api/sessionApi';
import { INICIANDO, MUITAS_TENTATIVAS, SEM_CONEXAO } from './messages';

// O aviso de cada resposta recusada do `POST /auth/session` (spec `operator-login-ui`).
// O /entrar o mostra depois do envio, e também quando chega de uma página do link com o
// login recusado (design.md da change entrada-publica-painel, D3).

export type EntryFailure = HttpFailure | { kind: 'network' };

export type EntryNotice =
  | { tipo: 'erro'; texto: string }
  | { tipo: 'bloqueado'; minutos: number | null }
  | null;

export const INCOMPLETO = 'Informe e-mail e senha.';

/** Os minutos do bloqueio: o `Retry-After` arredondado para cima (o quadro diz "5 minutos" fixo; convenção 17). */
export function lockMinutes(retryAfter: number | undefined): number | null {
  return retryAfter === undefined ? null : Math.max(1, Math.ceil(retryAfter / 60));
}

export function entryNotice(result: EntryFailure): EntryNotice {
  if (result.kind === 'network') return { tipo: 'erro', texto: SEM_CONEXAO };
  switch (result.status) {
    case 400:
      return { tipo: 'erro', texto: result.title ?? INCOMPLETO };
    case 401:
      return { tipo: 'erro', texto: result.title ?? 'E-mail ou senha inválidos.' };
    case 403:
      // "Acesso desativado…" ou "Origem não permitida.": os dois são texto para a
      // pessoa, e o painel não precisa distinguir (D6).
      return { tipo: 'erro', texto: result.title ?? 'Acesso recusado.' };
    case 423:
      return { tipo: 'bloqueado', minutos: lockMinutes(result.retryAfter) };
    case 429:
      return { tipo: 'erro', texto: MUITAS_TENTATIVAS };
    case 503:
      return { tipo: 'erro', texto: INICIANDO };
    default:
      return { tipo: 'erro', texto: 'Não foi possível entrar. Tente de novo.' };
  }
}

/** O texto do aviso `bloqueado`, com N do `Retry-After`. */
export function lockText(minutos: number | null): string {
  const tempo = minutos === null ? 'alguns minutos' : `${minutos} ${minutos === 1 ? 'minuto' : 'minutos'}`;
  return `Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por ${tempo}.`;
}

/** Depois de `429` e `503`, o botão volta quando passa o `Retry-After`. */
export function blocksUntilRetry(result: EntryFailure): boolean {
  return result.kind === 'http' && (result.status === 429 || result.status === 503);
}

/**
 * O `state` do router com que as páginas do link abrem o /entrar: já no passo do código, já na
 * etapa do cadastro (change verificacao-2-etapas-obrigatoria, D7), ou com o e-mail e a recusa do
 * login (spec `panel-access-link-pages`). A senha nunca vai aqui.
 */
export type EntrarRouteState =
  | { etapa: 'codigo' }
  | { etapa: 'cadastro' }
  | { email: string; falha: EntryFailure };

export function readEntrarState(value: unknown): EntrarRouteState | null {
  const state = value as Partial<{ etapa: unknown; email: unknown; falha: unknown }> | null;
  if (state?.etapa === 'codigo') return { etapa: 'codigo' };
  if (state?.etapa === 'cadastro') return { etapa: 'cadastro' };
  const falha = state?.falha as EntryFailure | undefined;
  if (typeof state?.email === 'string' && (falha?.kind === 'network' || falha?.kind === 'http')) {
    return { email: state.email, falha };
  }
  return null;
}
