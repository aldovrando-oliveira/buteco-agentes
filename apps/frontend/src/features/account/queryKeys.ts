// As chaves do TanStack Query dos cartões de Minha conta. Só leituras: o segredo e os
// códigos de recuperação nunca passam pelo cache (design.md da change minha-conta-painel, D5).
export const ACCOUNT_QUERY_KEY = ['account'] as const;
export const TWO_FACTOR_QUERY_KEY = ['account', 'two-factor'] as const;
export const SESSIONS_QUERY_KEY = ['account', 'sessions'] as const;
