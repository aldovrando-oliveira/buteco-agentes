import type { McpServerAuthType } from '../types/mcpServer';

// Rótulo de cada tipo de autenticação, num mapa só. Eram duas cópias
// (`McpServerTable` e `McpServerConfigCard`), e o formulário seria a terceira
// (cartões e avisos de troca de tipo). Três cópias é o gatilho da convenção 2
// (design.md da change frontend-mcp-autenticacao-header-customizado, D7). O
// `Record` exaustivo faz o `tsc` reprovar quando um tipo novo entra sem rótulo,
// que é o que deixou `CustomHeader` com rótulo vazio entre a #160 e a #168.
//
// O valor no fio continua sendo a chave (`CustomHeader`); o rótulo é só da tela
// (decisão 1 do dono na #168).
export const authTypeLabels: Record<McpServerAuthType, string> = {
  None: 'Nenhuma',
  BearerToken: 'Bearer Token',
  CustomHeader: 'Token em header customizado',
};
