import type { Papel } from '../auth/roles';

// Os papéis que entram em cada grupo de rotas da guarda (spec `panel-roles`, "Guarda de rota
// por papel"). Ficam fora do `RequirePapel.tsx` porque o Fast Refresh pede arquivo de
// componente só com componentes.

/** Só o Admin: `/users`, `/users/:id`, `/identity`. */
export const SO_ADMIN: readonly Papel[] = ['admin'];
/** Escrita, histórico de conversas e conectores: Admin e Operador. */
export const ADMIN_E_OPERADOR: readonly Papel[] = ['admin', 'operador'];
