import {
  CONVITE_ACEITO_COM_CADASTRO,
  GOOGLE_CONFIRMOU_COM_CADASTRO,
  GOOGLE_CONFIRMOU_COM_CODIGO,
} from './federatedNotices';

// O que a volta do provedor traz ao /entrar na query (design.md da change telas-login-google, D1 e
// D2): `federado` e `segundoPasso` (o login aceito), `convite=aceito` (o aceite do convite), ou
// `recusa` e `provedor` (a recusa). Lido uma vez, na montagem; depois a página troca o endereço sem
// esses parâmetros, mantendo o `retorno` (Q4).

/** Os parâmetros da volta, que saem do endereço depois de lidos. */
export const RETURN_PARAMS = ['federado', 'segundoPasso', 'convite', 'recusa', 'provedor'] as const;

export interface FederatedReturn {
  /** A etapa que a volta abre; `null` sem volta, ou com `segundoPasso` desconhecido (convenção 13). */
  etapa: 'codigo' | 'cadastro' | null;
  /** O aviso verde acima da etapa. */
  aviso: string | null;
  /** O aceite do convite: a chave do token sai antes do cadastro. */
  conviteAceito: boolean;
  /** O motivo da recusa, como veio (o desconhecido sai como a falha). */
  recusa: string | null;
  /** A chave do provedor da recusa, como veio; confere a recusa gravada (D2). */
  provedor: string | null;
  /** Se o endereço tem parâmetros da volta a limpar. */
  sujo: boolean;
}

export function readFederatedReturn(params: URLSearchParams): FederatedReturn {
  const sujo = RETURN_PARAMS.some((name) => params.has(name));
  const recusa = params.get('recusa');
  const provedor = recusa === null ? null : params.get('provedor');
  const federado = params.has('federado');
  const passo = params.get('segundoPasso');
  const conviteAceito = federado && passo === 'cadastro' && params.get('convite') === 'aceito';

  if (!federado || recusa !== null) {
    return { etapa: null, aviso: null, conviteAceito: false, recusa, provedor, sujo };
  }
  if (passo === 'totp') {
    return {
      etapa: 'codigo',
      aviso: GOOGLE_CONFIRMOU_COM_CODIGO,
      conviteAceito: false,
      recusa: null,
      provedor: null,
      sujo,
    };
  }
  if (passo === 'cadastro') {
    return {
      etapa: 'cadastro',
      aviso: conviteAceito ? CONVITE_ACEITO_COM_CADASTRO : GOOGLE_CONFIRMOU_COM_CADASTRO,
      conviteAceito,
      recusa: null,
      provedor: null,
      sujo,
    };
  }
  return { etapa: null, aviso: null, conviteAceito: false, recusa: null, provedor: null, sujo };
}

/** A query sem os parâmetros da volta, com o resto (o `retorno`, o `voltarPara`) como veio. */
export function withoutReturnParams(params: URLSearchParams): string {
  const kept = new URLSearchParams(params);
  for (const name of RETURN_PARAMS) kept.delete(name);
  const query = kept.toString();
  return query ? `?${query}` : '';
}
