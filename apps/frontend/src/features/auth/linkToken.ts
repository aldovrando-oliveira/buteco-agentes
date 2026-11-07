import { useEffect, useState } from 'react';

// O token dos links de uso único (`<painel>/convite#<token>` e
// `<painel>/redefinir-senha#<token>`, #181 D3). O fragmento não sai do navegador, mas
// fica no histórico da aba: a página o lê uma vez, troca o endereço sem ele antes de
// qualquer pedido, e o guarda no sessionStorage da aba, para a recarga funcionar. A chave
// é apagada no sucesso, no link inválido e ao sair da página (decisão do dono, Q4; design.md
// da change entrada-publica-painel, D4). O token só vai no corpo dos POSTs.

export type LinkPage = 'convite' | 'redefinir-senha';

export const LINK_TOKEN_KEYS: Record<LinkPage, string> = {
  convite: 'buteco.link.convite',
  'redefinir-senha': 'buteco.link.redefinir-senha',
};

function store(page: LinkPage, token: string) {
  try {
    window.sessionStorage.setItem(LINK_TOKEN_KEYS[page], token);
  } catch {
    // Sem sessionStorage (modo privado restrito), o token fica só na memória da página.
  }
}

function stored(page: LinkPage): string | null {
  try {
    return window.sessionStorage.getItem(LINK_TOKEN_KEYS[page]);
  } catch {
    return null;
  }
}

export function clearLinkToken(page: LinkPage) {
  try {
    window.sessionStorage.removeItem(LINK_TOKEN_KEYS[page]);
  } catch {
    // nada a apagar
  }
}

/**
 * Lê o token do fragmento, se houver, grava-o na chave da página e tira o fragmento do
 * endereço (mantendo o `state` do router); depois devolve o token da chave. Idempotente:
 * a segunda chamada já não acha o fragmento, mas acha a chave.
 */
export function takeLinkToken(page: LinkPage): string | null {
  const fragment = window.location.hash.slice(1);
  if (fragment) {
    store(page, fragment);
    window.history.replaceState(window.history.state, '', `${window.location.pathname}${window.location.search}`);
    return stored(page) ?? fragment;
  }
  return stored(page);
}

/**
 * O token da página, lido na primeira renderização (antes do primeiro pedido, que sai de
 * um efeito). O efeito o grava a partir do estado e o apaga na limpeza: ao desmontar, a
 * chave sai; no StrictMode, montar → limpar → montar termina com a chave gravada, porque o
 * estado sobrevive à remontagem simulada. Recarregar a página não roda a limpeza.
 */
export function useLinkToken(page: LinkPage): string | null {
  const [token] = useState(() => takeLinkToken(page));

  useEffect(() => {
    if (token) store(page, token);
    return () => clearLinkToken(page);
  }, [page, token]);

  return token;
}
