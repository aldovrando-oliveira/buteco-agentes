import { httpFailure, postJson, type HttpFailure } from './sessionApi';

// As rotas públicas do link de uso único (#181, D6, D7 e D10), anônimas, com JSON e a
// `Origin` conferida como no login. Todas são POST com o token no corpo: um GET com o
// token no caminho anularia o fragmento (#181 D3). Sem token de sessão e sem
// `sessionFetch`, como o `sessionApi`; o resultado é um valor por status, porque cada um
// tem um estado de tela (design.md da change entrada-publica-painel, D7).

export type LinkPurpose = 'convite' | 'redefinir-senha';

const ROUTES: Record<LinkPurpose, { lookup: string; complete: string }> = {
  convite: { lookup: '/auth/invitation', complete: '/auth/invitation/accept' },
  'redefinir-senha': { lookup: '/auth/password-reset', complete: '/auth/password-reset/complete' },
};

export interface LinkDetails {
  nome: string;
  email: string;
  /** Só no convite: o nome de quem convidou. */
  convidadoPor?: string;
  expiraEm: string;
}

export type LookupResult = { kind: 'ok'; link: LinkDetails } | HttpFailure | { kind: 'network' };
export type CompleteResult = { kind: 'ok' } | HttpFailure | { kind: 'network' };

export async function lookupLink(purpose: LinkPurpose, token: string): Promise<LookupResult> {
  const response = await postJson(ROUTES[purpose].lookup, { token });
  if (response === null) return { kind: 'network' };
  if (response.status !== 200) return httpFailure(response);

  const body = (await response.json().catch(() => undefined)) as Partial<Record<keyof LinkDetails, unknown>> | undefined;
  if (typeof body?.nome !== 'string' || typeof body.email !== 'string' || typeof body.expiraEm !== 'string') {
    return { kind: 'http', status: 200 };
  }
  return {
    kind: 'ok',
    link: {
      nome: body.nome,
      email: body.email,
      expiraEm: body.expiraEm,
      convidadoPor: typeof body.convidadoPor === 'string' ? body.convidadoPor : undefined,
    },
  };
}

/** O aceite do convite ou a conclusão da redefinição: `204` sem sessão (#181 D6, P5). */
export async function completeLink(purpose: LinkPurpose, token: string, senha: string): Promise<CompleteResult> {
  const response = await postJson(ROUTES[purpose].complete, { token, senha });
  if (response === null) return { kind: 'network' };
  if (response.status === 204) return { kind: 'ok' };
  return httpFailure(response);
}
