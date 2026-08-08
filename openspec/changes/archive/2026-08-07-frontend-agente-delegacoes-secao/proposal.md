## Why

O backend já expõe `PUT /agents/{id}/delegations` e o campo `delegatesTo`
em `AgentResponse` (change `backend-agente-delegacao-catalogo-vinculo`,
já aplicada), mas não existe nenhuma interface para gerenciar essas
delegações de saída. Sem esta peça, o cadastro de delegação entre
agentes só é acessível via chamada direta à API — a linha de delegação
fica incompleta do ponto de vista do usuário do `apps/frontend`.

## What Changes

- Novo componente `AgentDelegationsSection` em
  `features/agents/components/`, renderizado dentro de
  `AgentDetailPage.tsx` logo após `AgentDetailCard`: seção sempre
  editável (sem alternância entre modo leitura/edição) com um
  `MultiSelect` do Mantine para escolher os agentes-alvo de delegação,
  pré-selecionado com `agent.delegatesTo`, excluindo o próprio agente das
  opções, com indicador visual para agentes inativos, botão "Salvar
  delegações" e botão "Cancelar" que restaura a seleção original sem
  requisição.
- Nova função `replaceAgentDelegations` em
  `features/agents/api/agentsApi.ts` e novo hook
  `useReplaceAgentDelegationsMutation` em
  `features/agents/api/useAgents.ts`, consumindo
  `PUT /agents/{id}/delegations` com `{ targetAgentIds: [...] }`.
- `Agent` (em `features/agents/types/agent.ts`) ganha o campo
  `delegatesTo: AgentSummaryReference[]` (`{ id, name }[]`), espelhando o
  `AgentSummaryResponse` já retornado pelo backend.
- Nenhuma detecção de ciclo de delegação na interface — permitido
  registrar A→B→C→A sem aviso, mesma postura já adotada pelo backend no
  cadastro.

## Capabilities

### New Capabilities
- `agent-delegation-binding-ui`: interface em `apps/frontend`, dentro de
  `AgentDetailPage`, para visualizar e substituir o conjunto de
  delegações de saída de um agente.

### Modified Capabilities
(nenhuma)

## Impact

- **apps/frontend**: novo componente `AgentDelegationsSection`; novas
  funções em `agentsApi.ts` e `useAgents.ts`; `Agent` type atualizado;
  `AgentDetailPage.tsx` passa a renderizar a nova seção. Nenhuma rota
  nova, nenhuma dependência nova.
- **apps/api / apps/workers**: nenhuma mudança — a change consome
  exclusivamente o endpoint já existente
  (`PUT /agents/{id}/delegations`).
