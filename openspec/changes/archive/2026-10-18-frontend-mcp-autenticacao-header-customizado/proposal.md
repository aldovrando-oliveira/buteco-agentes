**Issue:** #168

## Why

A #160 entregou o tipo de autenticação `CustomHeader` em `apps/api` e
`apps/workers` (change arquivada
`openspec/changes/archive/2026-10-18-mcp-autenticacao-header-customizado/`), e
deixou a interface para esta issue (D7 daquela change). Até aqui o painel só
conhece `None` e `BearerToken`: um servidor `CustomHeader` criado pela API sai
com rótulo vazio na tabela e no card de detalhe, e o formulário de edição não
consegue salvá-lo, porque não tem o campo do nome do header. Esta change fecha
essa janela.

Ela também corrige um defeito que já existe: em edição, trocar o tipo de
autenticação e deixar a credencial em branco faz "Testar conexão" testar o
servidor **salvo**, com o tipo antigo, e mostrar o resultado como se fosse o da
configuração na tela.

## What Changes

Tudo em `apps/frontend`. Nenhuma mudança em `apps/api`, `apps/workers`,
`apps/inbox` nem `apps/connectors`.

- **Seletor de tipo em cartões com radio** no formulário de criar e de editar
  servidor MCP, no lugar do `Select`: Nenhuma, Bearer Token e Token em header
  customizado, cada um com título e descrição curta. Abaixo de uma largura
  mínima os cartões empilham, e nenhum título quebra linha nem estoura o cartão.
- **Componente de cartão com radio compartilhado**, extraído do `OriginOption`
  de `KnowledgeBaseOriginCard.tsx`, que passa a usá-lo sem mudança de
  comportamento (`apps/frontend/src/components/forms/`).
- **Campo "Nome do header"**, obrigatório e monoespaçado, só com
  `CustomHeader`. É pré-preenchido na edição com o `authHeaderName` devolvido
  pela API e enviado só com esse tipo. Nesse tipo, o campo de credencial se
  chama "Token".
- **Caixa "Como a credencial sai em cada requisição ao servidor"**, só com
  `CustomHeader`: `<nome do header>: ••••••••`, com o nome acompanhando a
  digitação e o valor sempre mascarado.
- **Avisos de troca de tipo na edição** (D4 da #160): troca entre dois tipos
  com credencial avisa que a credencial salva não é reaproveitada e a torna
  obrigatória; troca para `None` avisa que a credencial atual será apagada;
  saída de `None` não tem aviso, só a credencial obrigatória.
- **"Testar conexão" na edição:** desabilitado quando o tipo mudou e a
  credencial nova está em branco. A nota de teste com a configuração salva
  passa a dizer "configuração" (o teste salvo também usa a URL e o nome do
  header salvos). O resultado do teste é descartado ao editar o nome do header.
- **Erros 400 do teste não salvo nos campos**, como já acontece no submit, no
  lugar do alerta "Falha na conexão" de hoje, que afirmava uma tentativa de
  conexão que não aconteceu.
- **Tabela e card de detalhe:** rótulo "Token em header customizado", e linha
  "Header" com o nome em monoespaçado no card, só com `CustomHeader`.
- **Tipos:** `McpServerAuthType` ganha `CustomHeader`; `McpServer`, os inputs de
  cadastro, edição e teste ganham `authHeaderName`.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `mcp-server-catalog-ui`: os requisitos de cadastro, detalhe, edição e teste
  de conexão passam a cobrir o tipo `CustomHeader` (seletor em cartões, nome do
  header, prévia, avisos de troca de tipo, teste desabilitado na troca sem
  credencial, erros 400 do teste nos campos). A listagem muda só o rótulo do
  tipo, que já está coberto pelo requisito de listagem, e não ganha delta.

## Impact

- **`apps/frontend`, código:**
  `features/mcp-servers/types/mcpServer.ts`,
  `components/McpServerForm.tsx`, `components/McpServerTable.tsx`,
  `components/McpServerConfigCard.tsx`, `pages/McpServerEditPage.tsx`;
  `features/knowledge-bases/components/KnowledgeBaseOriginCard.tsx` (passa a
  usar o componente extraído); `src/index.css` (a classe do cartão marcado muda
  de nome); componente novo em `src/components/forms/`; dois utilitários novos
  em `features/mcp-servers/utils/` (o mapa de rótulos do tipo, que passa a ter
  três consumidores, e a regra de qual teste de conexão usar).
- **`apps/frontend`, testes:** os dos arquivos acima, mais os fixtures de
  `McpServer` em outras features que o `tsc` enumerar ao tornar
  `authHeaderName` obrigatório no tipo (`agents`, `inventory`).
- **Contrato:** nenhum novo. A change consome o que a #160 já serve
  (`authHeaderName` em `McpServerResponse` e nos três requests).
- **Fora de escopo:** OAuth client credentials (#161).
