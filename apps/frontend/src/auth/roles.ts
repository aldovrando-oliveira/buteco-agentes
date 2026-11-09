// O papel no painel (spec `panel-roles`; design.md da change papeis-no-painel, D1). O papel tem
// dois vocabulários, e este é o único lugar que os conhece (decisão do dono, 08/11/2026, #184):
// o claim `role` do id token vem em inglês, como no banco (`admin`, `operator`, `reader`), e o
// campo `papel` das respostas e dos corpos vem em português, no padrão do `estado` (`admin`,
// `operador`, `leitor`). O `operator` do papel não é o subject `operator` do cliente do painel.
//
// Valor fora da tabela vira `null`, e nunca um dos três. Na tela, o `null` vale Leitor (o
// mínimo), até a renovação trazer um id token com papel (D1, Q2): tratar a ausência como Admin
// mostraria, por um instante, o que talvez não se possa.

export type Papel = 'admin' | 'operador' | 'leitor';

const DO_CLAIM: Record<string, Papel> = { admin: 'admin', operator: 'operador', reader: 'leitor' };
const DA_RESPOSTA: Record<string, Papel> = {
  admin: 'admin',
  operador: 'operador',
  leitor: 'leitor',
};
const ROTULO: Record<Papel, string> = { admin: 'Admin', operador: 'Operador', leitor: 'Leitor' };

/** O papel do claim `role` do id token; `null` fora de `admin`, `operator` e `reader`. */
export function papelDoClaim(role: unknown): Papel | null {
  return typeof role === 'string' && Object.hasOwn(DO_CLAIM, role) ? DO_CLAIM[role] : null;
}

/** O papel do campo `papel` de uma resposta; `null` fora de `admin`, `operador` e `leitor`. */
export function papelDaResposta(papel: unknown): Papel | null {
  return typeof papel === 'string' && Object.hasOwn(DA_RESPOSTA, papel) ? DA_RESPOSTA[papel] : null;
}

/** O valor do `papel` nos corpos da API, que é o próprio valor do tipo. */
export function papelParaCorpo(papel: Papel): string {
  return papel;
}

export function rotuloDoPapel(papel: Papel): string {
  return ROTULO[papel];
}

/** Os três papéis, na ordem dos seletores do protótipo. */
export const PAPEIS: readonly Papel[] = ['admin', 'operador', 'leitor'];

export interface Permissoes {
  /** Usuários, convites e Identidade (`/users/*`, `/identity`): só o Admin. */
  gerirAcesso: boolean;
  /** Toda escrita de agentes, MCP, conhecimento e canais: Admin e Operador. */
  escrever: boolean;
  /** O histórico de conversas (sessões e mensagens do canal): Admin e Operador. */
  verHistorico: boolean;
  /** Os conectores: o seletor de pastas e o "Sincronizar agora". Admin e Operador. */
  usarConectores: boolean;
}

const LEITOR: Permissoes = {
  gerirAcesso: false,
  escrever: false,
  verHistorico: false,
  usarConectores: false,
};
const OPERADOR: Permissoes = {
  gerirAcesso: false,
  escrever: true,
  verHistorico: true,
  usarConectores: true,
};
const ADMIN: Permissoes = {
  gerirAcesso: true,
  escrever: true,
  verHistorico: true,
  usarConectores: true,
};

/** O que a tela mostra para o papel; o desconhecido (`null`) tem as do Leitor. */
export function permissoes(papel: Papel | null): Permissoes {
  if (papel === 'admin') return ADMIN;
  if (papel === 'operador') return OPERADOR;
  return LEITOR;
}
