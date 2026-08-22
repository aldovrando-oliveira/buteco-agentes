// Módulo fininho, sem fetch (design.md, Decision 7) — importado por cada
// request<T> de feature para anexar o header Authorization e reagir a
// 401. Não é um cliente HTTP compartilhado; a convenção de cada feature
// manter seu próprio request<T>/ApiError continua valendo.
//
// sessionStorage, não localStorage: token de vida curta, sem necessidade
// de sobreviver ao fechamento da aba/navegador.
const STORAGE_KEY = 'buteco.operatorToken';

export function getToken(): string | null {
  return sessionStorage.getItem(STORAGE_KEY);
}

export function setToken(token: string): void {
  sessionStorage.setItem(STORAGE_KEY, token);
}

export function clearToken(): void {
  sessionStorage.removeItem(STORAGE_KEY);
}
