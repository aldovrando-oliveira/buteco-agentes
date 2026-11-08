export const USERS_QUERY_KEY = ['users'] as const;

/** O detalhe de um usuário, sob `['users']`: invalidar a lista também o relê. */
export const userQueryKey = (id: string) => ['users', id] as const;
