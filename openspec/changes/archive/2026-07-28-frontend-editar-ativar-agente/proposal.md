## Why

`apps/api` já expõe `PUT /agents/{id}`, `POST /agents/{id}/activate` e
`POST /agents/{id}/deactivate`, com `isActive` presente em `AgentResponse`
(change `apps-api-agent-update-status`, já arquivada). `apps/frontend` ainda
não consome nenhum dos três: a página de detalhe de agente é somente
leitura e não há como corrigir um agente cadastrado errado nem pausar o
recebimento de mensagens de um agente sem ação direta na API. Esta mudança
fecha esse ciclo, reaproveitando a infraestrutura já existente (roteamento,
estado de servidor via `@tanstack/react-query`, `AgentForm`,
`@mantine/notifications`) em vez de introduzir um padrão novo.

## What Changes

- Novo endpoint consumido `PUT /agents/{id}` via nova página
  `AgentEditPage` em `/agents/:id/edit`, reaproveitando `AgentForm.tsx`
  (ganha suporte a valores iniciais e rótulo de submit configurável, em vez
  de duplicar o formulário).
- Novos endpoints consumidos `POST /agents/{id}/activate` e
  `POST /agents/{id}/deactivate`, acionados por botões em
  `AgentDetailPage` (a lógica de ativar/desativar vive na página, não no
  componente apresentacional `AgentDetailCard`).
- Desativar um agente exige confirmação explícita via `Modal` do
  `@mantine/core` antes de enviar a requisição — ativar não exige
  confirmação. Justificativa completa em `design.md`.
- `isActive` passa a ser visível na interface: badge em `AgentDetailCard` e
  em cada linha de `AgentTable`.
- `AgentDetailPage` deixa de ser somente leitura: ganha link para edição e
  os botões de ativar/desativar.
- Notificações via `@mantine/notifications` para as três operações novas
  (atualizado / ativado / desativado), mesmo padrão já usado no cadastro.
- Tipos (`features/agents/types/agent.ts`): `Agent` ganha `isActive:
  boolean`; novo `UpdateAgentInput`.
- Estado de servidor (`agentsApi.ts` e `useAgents.ts`): `updateAgent`,
  `activateAgent`, `deactivateAgent` e os hooks de mutation
  correspondentes, com a mesma estratégia de cache já usada em
  `useCreateAgentMutation` (`setQueryData` no agente + invalidação da
  lista).

## Capabilities

### New Capabilities
(nenhuma — esta mudança estende a capability já existente)

### Modified Capabilities
- `agent-catalog-ui`:
  - "Detalhe de agente somente leitura" deixa de ser somente leitura: passa
    a expor link de edição, indicador de `isActive` e ações de
    ativar/desativar (com confirmação na desativação).
  - "Listagem de agentes na interface" passa a exibir o `isActive` de cada
    agente na tabela.
  - Novo requirement de edição de agente pela interface (`PUT
    /agents/{id}`).
  - Novo requirement de ativação/desativação de agente pela interface
    (`POST /agents/{id}/activate|deactivate`).

## Impact

- **apps/frontend** (único app afetado; nenhuma mudança em apps/api ou
  apps/workers nesta fatia):
  - `features/agents/types/agent.ts`: `isActive` em `Agent`, novo
    `UpdateAgentInput`.
  - `features/agents/api/agentsApi.ts` e `useAgents.ts`: três funções e
    três hooks de mutation novos.
  - `features/agents/components/AgentForm.tsx`: suporte a `initialValues` e
    rótulo de submit configurável (uso duplo: criação e edição).
  - `features/agents/components/AgentDetailCard.tsx`: badge de `isActive`
    (continua puramente apresentacional).
  - `features/agents/components/AgentTable.tsx`: coluna/indicador de
    `isActive` por linha.
  - `features/agents/pages/AgentDetailPage.tsx`: deixa de ser somente
    leitura; ganha link de edição, ações de ativar/desativar e o `Modal`
    de confirmação de desativação.
  - Nova `features/agents/pages/AgentEditPage.tsx` (`/agents/:id/edit`).
  - `app/router.tsx`: nova rota `agents/:id/edit`.
  - Testes (Vitest + Testing Library) estendidos/criados para todo o
    escopo acima, incluindo reescrita do teste existente de
    `AgentDetailPage` que hoje afirma "sem controles de edição/exclusão"
    (não é mais verdade).
- **Non-goals explícitos**: sem exclusão de agente (não existe no
  backend); sem ação em lote (ativar/desativar/editar múltiplos agentes de
  uma vez); sem tratamento de concorrência otimista (último write vence);
  sem biblioteca de ícones nova; sem mudança em `apps/api` ou
  `apps/workers` nesta fatia.
