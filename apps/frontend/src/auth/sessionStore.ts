// O registro da sessão do painel (design.md da change painel-entra-pelo-emissor, D2):
// um só, no localStorage, para a sessão ser do navegador e não da aba. Recarregar não
// desloga, aba nova não pede login, e o logout de uma aba chega às outras pelo evento
// `storage`, sem segundo canal que poderia divergir do registro (D3).
//
// Só a renovação, a troca do código, o logout e o fim de sessão escrevem, e sempre sob
// o lock da renovação (renewal.ts). Ler não pede lock.

export const SESSION_KEY = 'buteco.sessao';

export interface Session {
  accessToken: string;
  /** Instante, em ms desde a época, em que o access token vence. */
  accessExpiresAt: number;
  refreshToken: string;
  idToken: string;
  /** Do payload do id token; só para exibir (D2). */
  nome: string | null;
  email: string | null;
}

export interface SessionStore {
  read(): Session | null;
  /** O texto cru do registro: instantâneo estável para o `useSyncExternalStore`. */
  raw(): string | null;
  write(session: Session): void;
  remove(): void;
  /** Avisa quando o registro muda, nesta aba ou em outra. */
  subscribe(listener: () => void): () => void;
}

export interface TokenResponseBody {
  access_token: string;
  refresh_token?: string;
  id_token?: string;
  expires_in: number;
}

function isSession(value: unknown): value is Session {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.accessToken === 'string' &&
    typeof v.accessExpiresAt === 'number' &&
    typeof v.refreshToken === 'string' &&
    typeof v.idToken === 'string' &&
    (v.nome === null || typeof v.nome === 'string') &&
    (v.email === null || typeof v.email === 'string')
  );
}

export function parseSession(raw: string | null): Session | null {
  if (raw === null) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    return isSession(parsed) ? parsed : null;
  } catch {
    // Registro ilegível é ausência: a rota protegida manda para o /entrar, sem erro.
    return null;
  }
}

export function createSessionStore(
  storage: () => Storage,
  events: () => Pick<Window, 'addEventListener' | 'removeEventListener'>,
): SessionStore {
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach((listener) => listener());

  return {
    raw: () => storage().getItem(SESSION_KEY),
    read: () => parseSession(storage().getItem(SESSION_KEY)),
    write(session) {
      storage().setItem(SESSION_KEY, JSON.stringify(session));
      notify();
    },
    remove() {
      storage().removeItem(SESSION_KEY);
      notify();
    },
    subscribe(listener) {
      // O evento `storage` só dispara nas OUTRAS abas; a mudança desta aba chega
      // pelo `notify` acima. `key === null` é o `localStorage.clear()`.
      const onStorage = (event: StorageEvent) => {
        if (event.key === SESSION_KEY || event.key === null) listener();
      };
      listeners.add(listener);
      events().addEventListener('storage', onStorage);
      return () => {
        listeners.delete(listener);
        events().removeEventListener('storage', onStorage);
      };
    },
  };
}

function decodeJwtPayload(jwt: string): Record<string, unknown> {
  try {
    const part = jwt.split('.')[1] ?? '';
    const base64 = part.replace(/-/g, '+').replace(/_/g, '/');
    const binary = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='));
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
    const payload: unknown = JSON.parse(new TextDecoder().decode(bytes));
    return typeof payload === 'object' && payload !== null ? (payload as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/**
 * O registro a partir da resposta do `/connect/token`. O id token é decodificado sem
 * conferir a assinatura: ele chegou do próprio emissor, por TLS, na resposta do token
 * (OIDC Core 3.1.3.7, item 6), e `nome` e `email` só são exibidos (D2). Sem id token
 * ou refresh novos na resposta, ficam os anteriores.
 */
export function sessionFromTokenResponse(
  body: TokenResponseBody,
  now: number,
  previous?: Session,
): Session {
  const idToken = body.id_token ?? previous?.idToken ?? '';
  const claims = decodeJwtPayload(idToken);
  return {
    accessToken: body.access_token,
    accessExpiresAt: now + body.expires_in * 1000,
    refreshToken: body.refresh_token ?? previous?.refreshToken ?? '',
    idToken,
    nome: typeof claims.name === 'string' ? claims.name : (previous?.nome ?? null),
    email: typeof claims.email === 'string' ? claims.email : (previous?.email ?? null),
  };
}

export function isTokenResponseBody(value: unknown): value is TokenResponseBody {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.access_token === 'string' && typeof v.expires_in === 'number';
}
