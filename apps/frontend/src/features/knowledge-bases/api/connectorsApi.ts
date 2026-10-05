import type { ConnectorFolder, ConnectorProvider } from '../types/connectors';
import { getToken } from '../../../auth/token';

// CLIENTE DO apps/connectors, no molde do cliente do apps/inbox (sessionsApi.ts):
// request<T> e erro próprios, token do operador pelo módulo fino auth/token, sem
// cliente HTTP comum (convenção 7). Mora em `knowledge-bases` porque a feature se
// organiza por conceito de domínio, não por origem do dado: os consumidores são
// telas de base (frontend-cadastro-base-sincronizada, D2).
//
// O "Sincronizar agora" do detalhe (`POST /connectors/knowledge-bases/{id}/sync`,
// #107) é mais uma função sobre o mesmo request<T>, e reusa ConnectorsError e o
// texto por código de utils/connectorErrors.ts (contexto `sync-request`).
//
// O operador acessa três rotas do apps/connectors (connectors-api e
// knowledge-sync-cycle, tabela de subjects): provedores, pastas e o pedido de
// sincronização. A descrição de pasta é do `service:api` e responde 403 ao
// operador.

export class ConnectorsError extends Error {
  // "network": o fetch nem chegou a uma resposta — o "fora do ar" que a tela diz
  // explicitamente. "http": houve resposta, com o status e o `code` dela.
  readonly kind: 'network' | 'http';
  readonly status?: number;
  readonly code?: string;
  readonly detail?: string;
  // Extensões do ProblemDetails além de `code` e `detail` (ex.: `knowledgeBaseName`
  // no 409 do apps/api), para quem traduz o erro em texto.
  readonly extensions: Record<string, unknown>;

  constructor(
    kind: 'network' | 'http',
    message: string,
    options: {
      status?: number;
      code?: string;
      detail?: string;
      extensions?: Record<string, unknown>;
    } = {},
  ) {
    super(message);
    this.name = 'ConnectorsError';
    this.kind = kind;
    this.status = options.status;
    this.code = options.code;
    this.detail = options.detail;
    this.extensions = options.extensions ?? {};
  }
}

// Três estados, e o primeiro é o que os outros endereços de build não têm
// (design.md, D1):
//   - `undefined` (variável ausente no build) → `null`: o painel não conhece o
//     apps/connectors, a origem Sincronizada fica indisponível e NENHUMA
//     requisição sai. SEM fallback para localhost, ao contrário de
//     VITE_API_BASE_URL: um build de produção sem a variável chamaria
//     http://localhost:5037 no navegador do operador.
//   - `""` → caminho relativo, mesmo domínio (stack com nginx, depois da #119).
//   - URL absoluta → aquele endereço.
// Lido por função, e não numa constante de módulo, para os testes trocarem o
// valor com `vi.stubEnv`.
export function connectorsBaseUrl(): string | null {
  return import.meta.env.VITE_CONNECTORS_BASE_URL ?? null;
}

const PROBLEM_FIELDS = new Set(['type', 'title', 'status', 'code', 'detail', 'instance']);

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const baseUrl = connectorsBaseUrl();
  if (baseUrl === null) {
    // Guarda contra chamada sem endereço: a tela não chega aqui sem ele, mas se
    // chegar, nada sai para a rede.
    throw new ConnectorsError('network', 'Endereço do apps/connectors não configurado no build.');
  }

  const token = getToken();
  let response: Response;
  try {
    response = await fetch(`${baseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...init?.headers,
      },
    });
  } catch {
    throw new ConnectorsError('network', `Falha de rede ao chamar ${path}`);
  }

  // 401 NÃO limpa o token nem redireciona para /login, ao contrário dos clientes
  // do apps/api e do apps/inbox. Aqui ele também pode ser chave de assinatura
  // divergente entre os processos, e deslogar a cada abertura do seletor seria um
  // laço sem saída; token vencido de verdade é pego pela próxima chamada ao
  // apps/api, que já desloga (design.md, D2).
  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as
      Record<string, unknown> | undefined;
    const extensions = Object.fromEntries(
      Object.entries(problem ?? {}).filter(([key]) => !PROBLEM_FIELDS.has(key)),
    );
    throw new ConnectorsError('http', `Erro ${response.status} ao chamar ${path}`, {
      status: response.status,
      code: typeof problem?.code === 'string' ? problem.code : undefined,
      detail: typeof problem?.detail === 'string' ? problem.detail : undefined,
      extensions,
    });
  }

  // 202 e 204 chegam sem corpo: o pedido de sincronização responde 202 vazio
  // (knowledge-sync-cycle), e ler JSON dele rejeitaria uma resposta de sucesso.
  if (response.status === 202 || response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

export function listConnectorProviders(): Promise<ConnectorProvider[]> {
  return request<ConnectorProvider[]>('/connectors/providers');
}

// Sem `parentId`: Drives Compartilhados e pastas compartilhadas com a conta. Com
// `parentId`: as subpastas dele. Provedor escapado no caminho e id na query, os
// dois opacos.
export function listConnectorFolders(
  providerKey: string,
  parentId?: string | null,
): Promise<ConnectorFolder[]> {
  const path = `/connectors/providers/${encodeURIComponent(providerKey)}/folders`;
  const query = parentId ? `?${new URLSearchParams({ parentId }).toString()}` : '';
  return request<ConnectorFolder[]>(`${path}${query}`);
}

// Dispara o ciclo da base em segundo plano. O 202 diz só que o pedido foi aceito
// — inclusive quando um ciclo da base já está em curso, e então nenhum outro é
// disparado (knowledge-sync-cycle, "Sincronizar agora"). O fim do ciclo se lê no
// apps/api, pela mudança de `syncState.lastFinishedAt` (utils/syncState.ts).
export function requestKnowledgeBaseSync(knowledgeBaseId: string): Promise<void> {
  return request<void>(`/connectors/knowledge-bases/${encodeURIComponent(knowledgeBaseId)}/sync`, {
    method: 'POST',
  });
}
