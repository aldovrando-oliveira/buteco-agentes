import type { McpServerAuthType } from '../types/mcpServer';

// Qual teste de conexão o formulário faz:
// - `saved`: `POST /mcp-servers/{id}/test`, com a configuração SALVA (URL, tipo,
//   nome do header e credencial);
// - `unsaved`: `POST /mcp-servers/test`, com o que está digitado;
// - `blocked`: nenhum, e o botão fica desabilitado.
//
// Função pura porque a mesma pergunta decide a rota, o `disabled` do botão e a
// nota da tela, e assim ela se testa sem montar o formulário (convenção 20).
export type ConnectionTestMode = 'saved' | 'unsaved' | 'blocked';

export interface ConnectionTestModeInput {
  isEditing: boolean;
  hasServerId: boolean;
  // Tipo do servidor como está gravado; em edição, o `authType` dos
  // `initialValues` que a página passa a partir de `GET /mcp-servers/{id}`.
  savedAuthType: McpServerAuthType;
  authType: McpServerAuthType;
  credential: string;
}

// Duas mudanças de comportamento em relação à regra anterior, que era "em
// edição, credencial em branco testa o salvo" (design.md da change
// frontend-mcp-autenticacao-header-customizado, D4):
//
// 1. Tipo trocado e credencial em branco é `blocked`, não `saved`. O teste
//    salvo usaria o tipo ANTIGO e mostraria o resultado como se fosse o da
//    tela — o defeito que a #168 corrige. E a credencial salva não serve ao
//    tipo novo: a API a apaga na troca de tipo (D4 da #160).
// 2. `None` é sempre `unsaved`. Sem credencial a preservar, o teste do que
//    está digitado é completo; testar o salvo ali usava a URL gravada sem nota
//    nenhuma na tela, que é afirmar em silêncio um resultado sobre outra URL
//    (convenção 13). Aprovada pelo dono em 18/10/2026.
export function connectionTestMode({
  isEditing,
  hasServerId,
  savedAuthType,
  authType,
  credential,
}: ConnectionTestModeInput): ConnectionTestMode {
  if (!isEditing || !hasServerId || authType === 'None' || credential.trim()) {
    return 'unsaved';
  }

  return authType === savedAuthType ? 'saved' : 'blocked';
}
