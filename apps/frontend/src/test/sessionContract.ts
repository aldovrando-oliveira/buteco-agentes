import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { authRuntime } from '../auth/session';
import { SESSION_KEY } from '../auth/sessionStore';
import { jsonResponse, signInForTest, signOutForTest, tokenResponse } from './authFakes';

/**
 * O `401` de uma cópia do `request<T>` (spec `panel-issuer-session`, "401 renova uma
 * vez antes de encerrar a sessão"): renova pelo `/connect/token` e repete uma vez com
 * o token novo; o segundo `401` encerra a sessão, menos no apps/connectors
 * (`endsSession: false`). Cada cópia chama este contrato com uma função dela.
 */
export function sessionFetchContract(call: () => Promise<unknown>, { endsSession = true } = {}) {
  const assign = vi.fn<(url: string) => void>();
  const apiAuth: Array<string | undefined> = [];

  function stub(status: (auth: string | undefined) => number) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string, init?: RequestInit) => {
        if (input === '/connect/token') return tokenResponse(2);
        const auth = (init?.headers as Record<string, string> | undefined)?.Authorization;
        apiAuth.push(auth);
        return jsonResponse(status(auth) === 401 ? { title: 'unauthorized' } : {}, status(auth));
      }),
    );
  }

  beforeEach(() => {
    apiAuth.length = 0;
    assign.mockReset();
    vi.spyOn(authRuntime, 'assign').mockImplementation(assign);
    signInForTest('access-1');
  });

  afterEach(() => {
    signOutForTest();
  });

  it('401 renova a sessão uma vez e repete com o token novo, sem deslogar', async () => {
    stub((auth) => (auth === 'Bearer access-1' ? 401 : 200));

    await call();

    expect(apiAuth).toEqual(['Bearer access-1', 'Bearer access-2']);
    expect(JSON.parse(localStorage.getItem(SESSION_KEY)!).accessToken).toBe('access-2');
    expect(assign).not.toHaveBeenCalled();
  });

  if (endsSession) {
    it('o segundo 401 encerra a sessão e vai ao /entrar com motivo e voltarPara', async () => {
      stub(() => 401);

      await expect(call()).rejects.toMatchObject({ status: 401 });

      expect(apiAuth).toHaveLength(2);
      expect(localStorage.getItem(SESSION_KEY)).toBeNull();
      expect(assign).toHaveBeenCalledWith(expect.stringMatching(/^\/entrar\?motivo=sessao-expirada&voltarPara=/));
    });
  } else {
    it('o segundo 401 não encerra a sessão: o registro continua e não navega', async () => {
      stub(() => 401);

      await expect(call()).rejects.toMatchObject({ status: 401 });

      expect(apiAuth).toHaveLength(2);
      expect(JSON.parse(localStorage.getItem(SESSION_KEY)!).accessToken).toBe('access-2');
      expect(assign).not.toHaveBeenCalled();
    });
  }
}
