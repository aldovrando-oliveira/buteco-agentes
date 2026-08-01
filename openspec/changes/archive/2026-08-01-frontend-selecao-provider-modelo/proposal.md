## Why

A change `backend-multi-provedor-llm` (arquivada) tornou `provider`/`model`
obrigatórios em `POST /agents` e `PUT /agents/{id}` e adicionou
`GET /providers` para listar o que está configurado no ambiente. A
interface de `apps/frontend` não foi atualizada: `AgentForm.tsx` só tem
Name/Instructions, então toda tentativa de criar ou editar um agente pela
UI atual falha com HTTP 400. Nenhum agente existente tem `provider`/`model`
preenchidos (sem backfill), então esse bloqueio afeta 100% do catálogo
hoje.

## What Changes

- `AgentForm` ganha dois campos `Select` (Provider e Model), alimentados
  por um novo `useProvidersQuery()` que consome `GET /providers`. Escolher
  um Provider filtra as opções de Model disponíveis; trocar o Provider
  depois de já ter um Model selecionado reseta o Model (exceto no mount
  inicial em modo edição).
- Ao editar um agente cujo `provider`/`model` atual não está mais entre as
  opções disponíveis, o Select correspondente exibe o valor atual como uma
  opção informativa e desabilitada (não re-selecionável), em vez de nascer
  vazio silenciosamente ou depender do comportamento default do componente.
- Quando `GET /providers` retorna lista vazia (nenhum provedor configurado
  no ambiente), o formulário de cadastro/edição é substituído por uma
  mensagem explicando a causa, em vez de exibir Selects vazios sem
  explicação.
- `AgentDetailCard` e `AgentTable` passam a exibir `provider`/`model` do
  agente como texto simples, com um indicador visual quando o agente
  precisa de reconfiguração (`provider`/`model` nulos).
- Validação client-side de Provider/Model obrigatórios, mesmo padrão já
  usado para nome/instruções; erro 400 do servidor nesses campos é mapeado
  para o Select correspondente, reaproveitando o mecanismo de mapeamento de
  erro já existente.
- Tipos de `features/agents/types/agent.ts` ganham `provider`/`model`.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `agent-catalog-ui`: os requirements de "Cadastro de agente pela
  interface", "Edição de agente pela interface", "Detalhe de agente" e
  "Listagem de agentes na interface" passam a incluir seleção/exibição de
  `provider`/`model`, incluindo os casos de provedor indisponível e
  catálogo de provedores vazio.

## Impact

- `apps/frontend`: `features/agents/api/` (novo `providersApi.ts` +
  `useProviders.ts`), `features/agents/components/AgentForm.tsx`,
  `AgentDetailCard.tsx`, `AgentTable.tsx`, `features/agents/pages/`
  (`AgentCreatePage.tsx`, `AgentEditPage.tsx`), `features/agents/types/agent.ts`,
  e os respectivos arquivos de teste.
- `apps/api`: nenhuma mudança — `GET /providers`, `POST /agents` e
  `PUT /agents/{id}` já existem e já validam `provider`/`model`.
- `apps/workers`: nenhuma mudança.
