// O `fetch` com a sessão (design.md da change painel-entra-pelo-emissor, D5 e D13).
// Cada `request<T>` de feature troca o `fetch` por este e continua dono da URL base,
// do erro, da leitura do corpo e do `204` (convenção 7): aqui não se monta URL, não se
// lê corpo e não se cria erro.
//
// Um `401` não é sempre sessão acabada: um `kid` novo no apps/inbox ou no
// apps/connectors responde `401` e dispara a busca das chaves, e o token pode ter
// vencido entre a conferência dos 60 s e a chegada do pedido. Por isso renova uma vez,
// com o token que falhou, e repete uma vez antes de desistir.
import { safeReturnPath } from './loginFlow';
import type { Renewal } from './renewal';
import type { AuthRuntime } from './runtime';

export interface SessionFetchOptions {
  /**
   * O segundo `401` encerra a sessão. Falso só no apps/connectors: uma configuração
   * errada dele (`aud`, issuer) responderia `401` a todo token, e deslogar a cada
   * abertura do seletor seria um laço sem saída; token vencido de verdade é pego pela
   * próxima chamada ao apps/api (D5, decisão do dono P5).
   */
  endsSessionOn401?: boolean;
}

export type SessionFetch = (
  input: string,
  init?: RequestInit,
  options?: SessionFetchOptions,
) => Promise<Response>;

// Objeto simples, como as cópias do `request<T>` já montam os cabeçalhos: um
// `Headers` passaria os nomes a minúsculas.
function withAuthorization(init: RequestInit | undefined, accessToken: string | undefined): RequestInit {
  const given = init?.headers;
  const base: Record<string, string> =
    given instanceof Headers || Array.isArray(given)
      ? Object.fromEntries(new Headers(given))
      : { ...(given as Record<string, string> | undefined) };
  return { ...init, headers: accessToken ? { ...base, Authorization: `Bearer ${accessToken}` } : base };
}

export function createSessionFetch(runtime: AuthRuntime, renewal: Renewal): SessionFetch {
  const sendToExpiredLogin = () =>
    runtime.assign(
      `/entrar?motivo=sessao-expirada&voltarPara=${encodeURIComponent(safeReturnPath(runtime.currentPath()))}`,
    );

  return async (input, init, { endsSessionOn401 = true } = {}) => {
    const session = await renewal.ensureFresh();
    const first = await runtime.fetch(input, withAuthorization(init, session?.accessToken));
    if (first.status !== 401) return first;

    const outcome = await renewal.renew(session?.accessToken);
    if (outcome.kind === 'ended') {
      // `invalid_grant` ou registro ausente: a sessão acabou de verdade, também para
      // quem não encerra no 401 (o registro já saiu).
      sendToExpiredLogin();
      return first;
    }
    // O emissor fora do ar não encerra nada (D4): a tela recebe o 401 como erro.
    if (outcome.kind === 'unavailable') return first;

    const second = await runtime.fetch(input, withAuthorization(init, outcome.session.accessToken));
    if (second.status === 401 && endsSessionOn401) {
      await renewal.endSessionIfCurrent(outcome.session.accessToken);
      sendToExpiredLogin();
    }
    return second;
  };
}
