## 1. Tipos (apps/frontend)

- [x] 1.1 Em `features/agents/types/agent.ts`, adicionar
  `AgentSummaryReference { id: string; name: string }` e o campo
  `delegatesTo: AgentSummaryReference[]` em `Agent`.

## 2. API e hooks (apps/frontend)

- [x] 2.1 Em `features/agents/api/agentsApi.ts`, adicionar
  `replaceAgentDelegations(agentId: string, targetAgentIds: string[]):
  Promise<Agent>`, chamando `PUT /agents/{agentId}/delegations` com body
  `{ targetAgentIds }`, seguindo o mesmo padrão de
  `replaceAgentMcpServers`.
- [x] 2.2 Em `features/agents/api/useAgents.ts`, adicionar
  `useReplaceAgentDelegationsMutation(agentId: string)`, seguindo o
  mesmo padrão de `useReplaceAgentMcpServersMutation` (`setQueryData` em
  `['agents', id]` e `invalidateQueries` em `['agents']` no `onSuccess`).
- [x] 2.3 Adicionar casos de teste em `useAgents.test.ts` para o novo
  hook (mutation dispara `replaceAgentDelegations` com os argumentos
  corretos, atualiza o cache em sucesso).

## 3. Componente AgentDelegationsSection (apps/frontend)

- [x] 3.1 Criar `features/agents/components/AgentDelegationsSection.tsx`
  como componente presentational: recebe `agent: Agent` e
  `agentsCatalog: Agent[]` via props (o catálogo completo, buscado pela
  página — nunca via `useAgentsQuery()` dentro do próprio componente).
  Sem import de nenhum hook de query/mutation de leitura de catálogo —
  mesma fronteira já usada entre `AgentCreatePage`/`AgentEditPage`
  (chamam `useProvidersQuery()`) e `AgentForm` (recebe `providers` via
  prop) — ver Decision 6 do design.md.
- [x] 3.2 Montar as opções do `MultiSelect` a partir de `agentsCatalog`,
  excluindo o próprio `agent.id`, com rótulo `"{name} (inativo)"` para
  agentes com `isActive: false`.
- [x] 3.3 Inicializar o estado de seleção com
  `agent.delegatesTo.map((d) => d.id)`.
- [x] 3.4 Implementar botão "Salvar delegações": chama
  `useReplaceAgentDelegationsMutation(agent.id)` (essa mutation, sim,
  pode ser chamada dentro do componente — mutations de escrita ficam
  junto da ação que dispara, diferente da query de leitura do catálogo)
  com a seleção atual, exibe notificação de sucesso ou notificação de
  erro genérica em `onError` (sem infraestrutura de erro por campo —
  Decision 5 do design.md), sem navegar para nenhuma outra rota.
- [x] 3.5 Implementar botão "Cancelar": restaura a seleção para
  `agent.delegatesTo.map((d) => d.id)`, sem enviar nenhuma requisição.
- [x] 3.6 Criar `AgentDelegationsSection.test.tsx` cobrindo: pré-seleção
  a partir de `delegatesTo`; agente atual ausente das opções; catálogo
  com um único agente cadastrado (o próprio) resulta em `MultiSelect`
  sem nenhuma opção disponível, sem quebrar a renderização da seção;
  agente inativo aparece como opção com indicador "(inativo)"; seleção/
  desseleção atualiza o estado local; salvar com um ou mais agentes
  selecionados envia `{ targetAgentIds: [...] }` com os ids
  correspondentes e mostra notificação de sucesso; salvar depois de
  desmarcar todos os agentes previamente selecionados envia
  `{ targetAgentIds: [] }` (array vazio explícito, nunca omitido nem
  `null`) e mostra notificação de sucesso; cancelar restaura a seleção
  original sem chamar `replaceAgentDelegations`; erro de submit exibe
  notificação genérica sem quebrar a seção.

## 4. Integração em AgentDetailPage (apps/frontend)

- [x] 4.1 Em `AgentDetailPage.tsx`, chamar `useAgentsQuery()` além do já
  existente `useAgentQuery(id)`, tratando o estado de carregamento/erro
  de ambas as queries antes de renderizar a seção — mesmo padrão já
  usado em `AgentMcpServersPage` (combina `useAgentQuery` e
  `useMcpServersQuery`).
- [x] 4.2 Renderizar
  `<AgentDelegationsSection agent={data} agentsCatalog={agentsQuery.data ?? []} />`
  imediatamente após `<AgentDetailCard agent={data} />`.
- [x] 4.3 Adicionar caso de teste em `AgentDetailPage.test.tsx`
  verificando que a seção de delegações aparece na página de detalhe do
  agente, posicionada depois do card de detalhe.
- [x] 4.4 Adicionar caso de teste em `AgentDetailPage.test.tsx` para o
  catálogo contendo apenas o próprio agente: a página renderiza
  normalmente, com a seção de delegações exibindo o `MultiSelect` sem
  nenhuma opção disponível (Scenario "Catálogo de agentes com um único
  agente cadastrado" do spec).

## 5. Verificação (apps/frontend)

- [x] 5.1 Rodar a suíte de testes do frontend (`npm run test` ou
  equivalente em `apps/frontend`) e confirmar que todos os testes novos
  e existentes passam.
- [x] 5.2 Rodar lint/typecheck do frontend e confirmar que não há erros
  introduzidos pelas mudanças desta change.
