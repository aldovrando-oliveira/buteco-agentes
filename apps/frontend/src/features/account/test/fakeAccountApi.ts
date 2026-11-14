import { vi } from 'vitest';
import { tokenResponse } from '../../../test/authFakes';

// O `fetch` falso de Minha conta, por método e caminho (sem a URL base nem a query), ligado
// ao `fetch` global que o `authRuntime` usa: o `sessionFetch` e o `request<T>` da feature
// correm de verdade em cima dele, e o `401` de recusa da rota passa pelo critério real
// (design.md da change minha-conta-painel, D10 e D11).
//
// Cada rota tem uma fila de respostas; a última fica valendo para os pedidos seguintes. Um
// pedido sem resposta falha o teste com o nome da rota.

export type FakeReply =
  | { status: number; body?: unknown; problem?: string; headers?: Record<string, string> }
  | Response
  | 'rede'
  | (() => Promise<FakeReply>);

export interface FakeRequest {
  method: string;
  path: string;
  body?: unknown;
  authorization?: string;
}

export function problem(status: number, title: string, headers: Record<string, string> = {}): FakeReply {
  return { status, problem: title, headers };
}

function toResponse(reply: Exclude<FakeReply, Response | 'rede' | (() => Promise<FakeReply>)>): Response {
  if (reply.problem !== undefined) {
    return new Response(JSON.stringify({ title: reply.problem, status: reply.status }), {
      status: reply.status,
      headers: { 'Content-Type': 'application/problem+json', ...reply.headers },
    });
  }
  if (reply.body === undefined) {
    return new Response(null, { status: reply.status, headers: reply.headers });
  }
  return new Response(JSON.stringify(reply.body), {
    status: reply.status,
    headers: { 'Content-Type': 'application/json', ...reply.headers },
  });
}

// O corpo em JSON das rotas da API, ou o texto cru do `/connect/token`, que é
// `application/x-www-form-urlencoded`: a renovação de um `403` passa por ele (change
// papeis-no-painel, D3), e um `JSON.parse` que lançasse deixaria a renovação repetindo
// por 25 s sem nunca chegar à rota falsa.
function parseBody(body: string): unknown {
  try {
    return JSON.parse(body);
  } catch {
    return body;
  }
}

export function fakeAccountApi(routes: Record<string, FakeReply | FakeReply[]> = {}) {
  // O emissor responde por padrão: todo `403` renova a sessão uma vez (change papeis-no-painel,
  // D3), e sem resposta a renovação repetiria por 25 s. O caso que quer outro emissor declara a
  // sua `POST /connect/token`.
  //
  // Mais três por padrão (change telas-contas-federadas, D2 e D10), que o caso sobrepõe ao declarar:
  // - `GET /account`, com SENHA: todo formulário com a confirmação de quem chama lê a conta para saber
  //   o modo, e sem a rota a consulta cairia em erro e o modo ficaria `'senha'` pelo caminho errado;
  // - as duas listas de provedores, VAZIAS: sem provedor, o cartão "Formas de entrada" de Minha conta
  //   não aparece, e o cartão dos provedores da Identidade não soma linha nem "Tentar de novo" (o
  //   `IdentityPage.test.tsx` conta as linhas da página inteira e procura o botão por papel).
  const comEmissor: Record<string, FakeReply | FakeReply[]> = {
    'POST /connect/token': () => Promise.resolve(tokenResponse(2)),
    'GET /account': { status: 200, body: ACCOUNT },
    'GET /auth/federated/providers': { status: 200, body: { provedores: [] } },
    'GET /identity/federated-providers': { status: 200, body: { provedores: [] } },
    ...routes,
  };
  const queues = new Map<string, FakeReply[]>(
    Object.entries(comEmissor).map(([key, reply]) => [key, Array.isArray(reply) ? [...reply] : [reply]]),
  );
  const requests: FakeRequest[] = [];

  const resolve = async (reply: FakeReply): Promise<Response> => {
    if (reply === 'rede') throw new TypeError('Failed to fetch');
    if (typeof reply === 'function') return resolve(await reply());
    if (reply instanceof Response) return reply.clone();
    return toResponse(reply);
  };

  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    const url = new URL(input, 'http://localhost');
    const method = (init?.method ?? 'GET').toUpperCase();
    const key = `${method} ${url.pathname}`;
    const headers = (init?.headers ?? {}) as Record<string, string>;
    requests.push({
      method,
      path: url.pathname,
      body: typeof init?.body === 'string' ? parseBody(init.body) : undefined,
      authorization: headers.Authorization,
    });
    const queue = queues.get(key);
    if (!queue || queue.length === 0) throw new Error(`fakeAccountApi: sem resposta para ${key}`);
    const reply = queue.length > 1 ? queue.shift()! : queue[0];
    return resolve(reply);
  });
  vi.stubGlobal('fetch', fetchMock);

  return {
    fetch: fetchMock,
    requests,
    /** Troca (ou acrescenta) a resposta de uma rota. */
    on(key: string, ...replies: FakeReply[]) {
      queues.set(key, replies);
    },
    /** Os pedidos a uma rota, na ordem. */
    calls(key: string) {
      const [method, path] = key.split(' ');
      return requests.filter((r) => r.method === method && r.path === path);
    },
  };
}

/** Uma conta Google conectada e disponível (`GET /account` e `GET /users/{id}`). */
export const GOOGLE_CONECTADA = {
  chave: 'google',
  tipo: 'google',
  email: 'ana@empresa.com',
  conectadaEm: '2026-08-14T12:00:00Z',
  ultimoUso: '2026-11-06T12:02:00Z',
  disponivel: true,
};

/** Um vínculo com um provedor que saiu da configuração: `chave` nula, sem ação (Q9). */
export const GOOGLE_FORA = {
  chave: null,
  tipo: 'google',
  email: 'ana@antiga.com.br',
  conectadaEm: '2026-09-02T12:00:00Z',
  ultimoUso: '2026-10-10T12:00:00Z',
  disponivel: false,
};

/** A lista anônima dos provedores com o Google configurado. */
export const PROVEDORES_GOOGLE = { provedores: [{ chave: 'google', tipo: 'google' }] };

/** As respostas de uma conta comum, para os cartões que o teste não exercita. */
export const ACCOUNT = {
  id: 'u-1',
  nome: 'Ana Souza',
  email: 'ana@exemplo.test',
  papel: 'admin',
  criadoEm: '2026-08-12T13:00:00Z',
  ultimoAcesso: '2026-11-06T17:15:00Z',
  duasEtapas: false,
  formasDeEntrada: ['senha'],
  contasConectadas: [],
};

/** A conta de Ana sem senha: entrou só pelo Google (change telas-contas-federadas). */
export const ACCOUNT_SO_GOOGLE = {
  ...ACCOUNT,
  formasDeEntrada: ['google'],
  contasConectadas: [GOOGLE_CONECTADA],
};

export const TWO_FACTOR_OFF = { ativa: false, ativadaEm: null, codigosRestantes: 0 };

export const SESSIONS = [
  { id: 's-1', criadaEm: '2026-11-06T12:02:00Z', ultimoUso: '2026-11-06T17:45:00Z', atual: true },
  { id: 's-2', criadaEm: '2026-10-28T21:30:00Z', ultimoUso: '2026-11-06T00:15:00Z', atual: false },
  { id: 's-3', criadaEm: '2026-10-20T11:45:00Z', ultimoUso: '2026-10-22T20:00:00Z', atual: false },
];

export function defaultRoutes(): Record<string, FakeReply> {
  return {
    'GET /account': { status: 200, body: ACCOUNT },
    'GET /account/two-factor': { status: 200, body: TWO_FACTOR_OFF },
    'GET /account/sessions': { status: 200, body: SESSIONS },
  };
}
