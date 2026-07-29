## Why

Feedback visual real de uso da interface de agentes (`apps/frontend`) revelou três
problemas de usabilidade: o formulário de agente não tem como cancelar e voltar, os
botões de ação na página de detalhe ficam escondidos abaixo de um card que cresce com
o tamanho do prompt, e o campo de instruções — que já é escrito em markdown pelos
usuários — é exibido como texto corrido, sem títulos, listas, negrito, tabelas ou
formatação nenhuma. Os dois últimos problemas interagem: renderizar o markdown sem
também revisitar o posicionamento dos botões pioraria o problema de rolagem, já que
títulos e listas ocupam mais altura vertical que texto corrido.

## What Changes

- `AgentForm` ganha um botão "Cancelar" (variant `default`, ao lado do botão de
  submit) e uma prop `onCancel: () => void` obrigatória.
- `AgentCreatePage` e `AgentEditPage` passam a fornecer `onCancel` com destino
  explícito (`/agents` e `/agents/{id}`, respectivamente — nunca histórico do
  navegador).
- `AgentDetailPage`: o bloco de ações (Editar, Ativar/Desativar) passa a ser
  renderizado antes do `AgentDetailCard`, não mais depois.
- `AgentDetailCard`: o campo `instructions` passa a ser renderizado como markdown
  (títulos, negrito, listas, tabelas, strikethrough, `---` como separador) via
  `react-markdown` + `remark-gfm`, envolvido em `Typography` do `@mantine/core`,
  dentro de um `ScrollArea` com altura máxima — para que um prompt longo não
  estique a página indefinidamente mesmo com os botões já reposicionados acima.
- Novas dependências em `apps/frontend`: `react-markdown@10.1.0` e
  `remark-gfm@4.0.1`.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `agent-catalog-ui`: os requisitos de "Detalhe de agente", "Edição de agente pela
  interface" e "Cadastro de agente pela interface" mudam — cancelar volta a um
  destino determinístico a partir do formulário, o bloco de ações do detalhe
  aparece antes do card de dados, e o conteúdo de instruções no detalhe é
  renderizado como markdown formatado em vez de texto corrido, com altura limitada
  e rolagem interna.

## Impact

- **apps/frontend** (única app afetada; sem mudanças em apps/api ou apps/workers):
  - `src/features/agents/components/AgentForm.tsx` — nova prop `onCancel`
    (obrigatória) e botão "Cancelar".
  - `src/features/agents/components/AgentDetailCard.tsx` — renderização de
    `instructions` via `react-markdown` + `remark-gfm` dentro de `Typography` +
    `ScrollArea`.
  - `src/features/agents/pages/AgentCreatePage.tsx` — passa `onCancel` navegando
    para `/agents`.
  - `src/features/agents/pages/AgentEditPage.tsx` — passa `onCancel` navegando
    para `/agents/{id}`.
  - `src/features/agents/pages/AgentDetailPage.tsx` — reordena o JSX: bloco de
    ações antes do `AgentDetailCard`.
  - `package.json` — adiciona `react-markdown@10.1.0` e `remark-gfm@4.0.1` às
    dependencies.
  - Testes: `AgentForm.test.tsx` (novo caso), `AgentDetailPage.test.tsx` (novo
    caso de ordem no DOM), `AgentDetailCard.test.tsx` (novo arquivo).
- Sem mudanças de API, contrato de rede ou schema — o campo `instructions` já
  existe e já é consumido como string; só a renderização no frontend muda.
