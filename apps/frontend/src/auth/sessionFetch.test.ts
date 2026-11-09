import { describe, expect, it, vi } from 'vitest';
import { createRenewal } from './renewal';
import { createSessionFetch } from './sessionFetch';
import { createSessionStore } from './sessionStore';
import { fakeRuntime, jsonResponse, session, tokenResponse } from '../test/authFakes';
import { papelDaSessao } from './session';
import type { AuthRuntime } from './runtime';

const API = 'http://localhost:5017';

/**
 * `fetch` falso com o `/connect/token` e uma rota de API: `api` diz, por token, o status
 * que a rota responde.
 */
function setup(
  api: (token: string | undefined) => number,
  token: () => Response = () => tokenResponse(2),
) {
  const calls: Array<{ url: string; auth?: string }> = [];
  const fetch = vi.fn<AuthRuntime['fetch']>(async (input, init) => {
    if (input === '/connect/token') return token();
    const auth = (init?.headers as Record<string, string> | undefined)?.Authorization;
    calls.push({ url: input, auth });
    return jsonResponse({ ok: true }, api(auth?.replace('Bearer ', '')));
  });
  const tab = fakeRuntime({ fetch, path: '/agents?filtro=x' });
  const store = createSessionStore(tab.runtime.storage, tab.runtime.events);
  const renewal = createRenewal(tab.runtime, store);
  return { ...tab, fetch, calls, store, sessionFetch: createSessionFetch(tab.runtime, renewal) };
}

describe('sessionFetch', () => {
  it('anexa o access token do registro e mantém os outros cabeçalhos', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 600_000));

    await t.sessionFetch(`${API}/agents`, { headers: { 'Content-Type': 'application/json' } });

    const init = t.fetch.mock.calls[0][1];
    expect(init?.headers).toEqual({
      'Content-Type': 'application/json',
      Authorization: 'Bearer access-1',
    });
  });

  it('sem registro, não anexa Authorization', async () => {
    const t = setup(() => 200);

    await t.sessionFetch(`${API}/agents`);

    expect(t.calls[0].auth).toBeUndefined();
  });

  it('renova antes do pedido quando o token vence em menos de 60 s', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 30_000));

    await t.sessionFetch(`${API}/agents`);

    expect(t.calls).toEqual([{ url: `${API}/agents`, auth: 'Bearer access-2' }]);
  });

  it('401 seguido de sucesso não desloga: renova uma vez e repete com o token novo', async () => {
    const t = setup((token) => (token === 'access-1' ? 401 : 200));
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(200);
    expect(t.calls.map((c) => c.auth)).toEqual(['Bearer access-1', 'Bearer access-2']);
    expect(t.store.read()?.accessToken).toBe('access-2');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  // A recusa da própria rota (design.md da change minha-conta-painel, D10; decisão do dono
  // Q0): o 401 com problem+json volta à tela como resposta, sem renovar nem repetir.
  it('401 com problem+json volta à tela sem renovar nem repetir', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 600_000));
    t.fetch.mockImplementation(async (input, init) => {
      if (input === '/connect/token') return tokenResponse(2);
      t.calls.push({
        url: input,
        auth: (init?.headers as Record<string, string> | undefined)?.Authorization,
      });
      return new Response(JSON.stringify({ title: 'Senha atual inválida.', status: 401 }), {
        status: 401,
        headers: { 'Content-Type': 'application/problem+json; charset=utf-8' },
      });
    });

    const response = await t.sessionFetch(`${API}/account/password`, { method: 'POST' });

    expect(response.status).toBe(401);
    expect(((await response.json()) as { title: string }).title).toBe('Senha atual inválida.');
    expect(t.calls).toEqual([{ url: `${API}/account/password`, auth: 'Bearer access-1' }]);
    expect(t.fetch.mock.calls.some(([input]) => input === '/connect/token')).toBe(false);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  // O 401 do JwtBearer: sem corpo e sem Content-Type.
  it('401 sem corpo continua renovando e repete com o token novo', async () => {
    const t = setup(() => 200);
    t.store.write(session(1, t.clock.now + 600_000));
    t.fetch.mockImplementation(async (input, init) => {
      if (input === '/connect/token') return tokenResponse(2);
      const auth = (init?.headers as Record<string, string> | undefined)?.Authorization;
      t.calls.push({ url: input, auth });
      return auth === 'Bearer access-1'
        ? new Response(null, { status: 401 })
        : new Response(null, { status: 204 });
    });

    const response = await t.sessionFetch(`${API}/account/password`, { method: 'POST' });

    expect(response.status).toBe(204);
    expect(t.calls.map((c) => c.auth)).toEqual(['Bearer access-1', 'Bearer access-2']);
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('segundo 401 encerra a sessão e vai ao /entrar com o voltarPara da rota atual', async () => {
    const t = setup(() => 401);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(2);
    expect(t.store.read()).toBeNull();
    expect(t.runtime.assign).toHaveBeenCalledWith(
      `/entrar?motivo=sessao-expirada&voltarPara=${encodeURIComponent('/agents?filtro=x')}`,
    );
  });

  it('no apps/connectors, o segundo 401 não encerra: o registro continua e não navega', async () => {
    const t = setup(() => 401);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/connectors/x`, undefined, {
      endsSessionOn401: false,
    });

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(2);
    expect(t.store.read()?.accessToken).toBe('access-2');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('invalid_grant na renovação encerra, também no apps/connectors', async () => {
    const t = setup(
      () => 401,
      () => jsonResponse({ error: 'invalid_grant' }, 400),
    );
    t.store.write(session(1, t.clock.now + 600_000));

    await t.sessionFetch(`${API}/connectors/x`, undefined, { endsSessionOn401: false });

    expect(t.store.read()).toBeNull();
    expect(t.runtime.assign).toHaveBeenCalledWith(
      expect.stringContaining('/entrar?motivo=sessao-expirada'),
    );
  });

  it('emissor indisponível depois do 401 não encerra: a tela recebe o 401', async () => {
    const t = setup(
      () => 401,
      () => jsonResponse({}, 503, { 'Retry-After': '30' }),
    );
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(401);
    expect(t.calls).toHaveLength(1);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('503 da rota não renova, não repete e não desloga', async () => {
    const t = setup(() => 503);
    t.store.write(session(1, t.clock.now + 600_000));

    const response = await t.sessionFetch(`${API}/agents`);

    expect(response.status).toBe(503);
    expect(t.calls).toHaveLength(1);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });
});

// O 403 (spec `panel-issuer-session`, "403 renova a sessão uma vez e não repete a chamada";
// design.md da change papeis-no-painel, D3, decisão do dono Q1).
describe('sessionFetch no 403', () => {
  /** A rota responde 403 a todo token; o `/connect/token` emite access-2, access-3, … */
  function forbidden(
    token: () => Response = (() => {
      let n = 1;
      return () => tokenResponse(++n);
    })(),
  ) {
    const t = setup(() => 403, token);
    t.store.write(session(1, t.clock.now + 600_000));
    const renovacoes = () =>
      t.fetch.mock.calls.filter(([input]) => input === '/connect/token').length;
    return { ...t, renovacoes };
  }

  it('Um 403, uma renovação, nenhuma repetição', async () => {
    const t = forbidden();

    const response = await t.sessionFetch(`${API}/agents/a1/deactivate`, { method: 'POST' });

    expect(response.status).toBe(403);
    expect(t.renovacoes()).toBe(1);
    expect(t.calls).toEqual([{ url: `${API}/agents/a1/deactivate`, auth: 'Bearer access-1' }]);
    expect(t.store.read()?.accessToken).toBe('access-2');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  // A renovação do 403 existe para trazer o papel gravado: o registro tem de terminar com o id
  // token NOVO. `renew()` sem o token que falhou reaproveitaria o registro ainda válido e não
  // buscaria nada (design.md da change papeis-no-painel, D4 e D12, premissa 3).
  it('A renovação depois do 403 traz o id token novo e o papel derivado', async () => {
    const t = forbidden(() => tokenResponse(2, 900, 'operator'));
    t.store.write(session(1, t.clock.now + 600_000, {}, 'admin'));
    const antes = t.store.read()!.idToken;
    expect(papelDaSessao(t.store.read())).toBe('admin');

    await t.sessionFetch(`${API}/users`);

    expect(t.renovacoes()).toBe(1);
    expect(t.store.read()!.idToken).not.toBe(antes);
    expect(papelDaSessao(t.store.read())).toBe('operador');
  });

  it('Dois 403 seguidos, uma renovação', async () => {
    const t = forbidden();

    await t.sessionFetch(`${API}/users`);
    await t.sessionFetch(`${API}/users`);
    await t.sessionFetch(`${API}/identity`);

    expect(t.renovacoes()).toBe(1);
    expect(t.calls.map((c) => c.auth)).toEqual([
      'Bearer access-1',
      'Bearer access-2',
      'Bearer access-2',
    ]);
  });

  it('A senha não é reenviada', async () => {
    const t = forbidden();
    const corpos: unknown[] = [];
    t.fetch.mockImplementation(async (input, init) => {
      if (input === '/connect/token') return tokenResponse(2);
      corpos.push(init?.body);
      return new Response(null, { status: 403 });
    });

    const body = JSON.stringify({
      nome: 'Marcos',
      email: 'm@exemplo.test',
      papel: 'operador',
      senha: 's3nha',
    });
    const response = await t.sessionFetch(`${API}/users/invitations`, { method: 'POST', body });

    expect(response.status).toBe(403);
    expect(corpos).toEqual([body]);
  });

  it('invalid_grant na renovação leva ao /entrar', async () => {
    const t = forbidden(() => jsonResponse({ error: 'invalid_grant' }, 400));

    const response = await t.sessionFetch(`${API}/users`);

    expect(response.status).toBe(403);
    expect(t.store.read()).toBeNull();
    expect(t.runtime.assign).toHaveBeenCalledWith(
      `/entrar?motivo=sessao-expirada&voltarPara=${encodeURIComponent('/agents?filtro=x')}`,
    );
  });

  it('emissor indisponível: a tela recebe o 403, sem /entrar', async () => {
    const t = forbidden(() => jsonResponse({}, 503, { 'Retry-After': '30' }));

    const response = await t.sessionFetch(`${API}/users`);

    expect(response.status).toBe(403);
    expect(t.store.read()?.accessToken).toBe('access-1');
    expect(t.runtime.assign).not.toHaveBeenCalled();
  });

  it('o 403 com problem+json também renova, e volta à tela com o corpo', async () => {
    const t = forbidden();
    t.fetch.mockImplementation(async (input) => {
      if (input === '/connect/token') return tokenResponse(2);
      return new Response(
        JSON.stringify({ title: 'Esta rota exige o login de um usuário ativo.', status: 403 }),
        {
          status: 403,
          headers: { 'Content-Type': 'application/problem+json' },
        },
      );
    });

    const response = await t.sessionFetch(`${API}/users`);

    expect(t.renovacoes()).toBe(1);
    expect(((await response.json()) as { title: string }).title).toBe(
      'Esta rota exige o login de um usuário ativo.',
    );
  });

  it('sem registro, o 403 não renova', async () => {
    const t = setup(() => 403);

    const response = await t.sessionFetch(`${API}/users`);

    expect(response.status).toBe(403);
    expect(t.fetch.mock.calls.some(([input]) => input === '/connect/token')).toBe(false);
  });
});
