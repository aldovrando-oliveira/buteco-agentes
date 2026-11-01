// Web Locks (a renovação coordenada, D3) e Web Crypto (o PKCE, D6) só existem em
// contexto seguro: HTTPS ou localhost. Sem um deles não há modo degradado que valha a
// pena, e o /entrar avisa em vez de começar o fluxo (design.md da change
// painel-entra-pelo-emissor, D3).
export function hasSecureContextApis(): boolean {
  return (
    typeof navigator !== 'undefined' &&
    typeof navigator.locks?.request === 'function' &&
    typeof globalThis.crypto?.subtle?.digest === 'function'
  );
}
