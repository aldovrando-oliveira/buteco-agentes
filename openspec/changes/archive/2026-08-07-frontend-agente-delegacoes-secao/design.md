## Context

`apps/frontend` já tem duas peças da linha de delegação/vínculo entre
agentes: a página de gestão de servidores MCP
(`AgentMcpServersPage.tsx`, rota própria `/agents/:id/mcp-servers`) e o
backend de delegação (`PUT /agents/{id}/delegations`, campo
`delegatesTo` em `AgentResponse`, ambos da change
`backend-agente-delegacao-catalogo-vinculo`, já aplicada). Falta a
última peça: uma interface para o usuário ver e editar para quais
agentes um agente delega.

`AgentDetailPage.tsx` hoje é majoritariamente leitura: `AgentDetailCard`
(nome, provider/model, servidores MCP vinculados como texto, instruções,
datas) mais um bloco de ações (`Editar`, `Gerenciar servidores MCP`,
`Ativar`/`Desativar`). A única ação direta na própria página é
ativar/desativar — desativar exige confirmação em modal porque tem
consequência distinta (mensagens futuras passam a ser rejeitadas);
ativar não exige.

Diferente do vínculo com MCP, delegação não tem descoberta ao vivo,
nem seleção por item vinculado com sub-opções — é um multi-select
simples contra o catálogo de agentes já carregado em outros lugares do
app (`useAgentsQuery()`, mesma fonte de `AgentListPage`). Isso não
justifica uma página própria.

## Goals / Non-Goals

**Goals:**
- Permitir ver e substituir o conjunto de delegações de saída de um
  agente sem sair de `AgentDetailPage`.
- Reaproveitar `useAgentsQuery()` como catálogo de opções, sem nova
  query.
- Seguir os padrões já estabelecidos no projeto (mutation hooks,
  notificações, mirror client-side de regra de servidor) em vez de
  introduzir um padrão novo sem necessidade.

**Non-Goals:**
- Nenhuma página ou rota nova.
- Nenhuma detecção ou aviso de ciclo de delegação na interface (Decision
  4).
- Nenhuma mudança em `apps/api` ou `apps/workers` — o endpoint já existe
  e já foi validado pela change `backend-agente-delegacao-catalogo-vinculo`.
- Nenhuma exibição de "quem delega para este agente" (direção reversa)
  — não existe endpoint para isso; non-goal já registrado na change do
  backend.
- Nenhuma linha de resumo textual de delegações em `AgentDetailCard`
  (ver Decision 1) — a seção nova, sempre visível e pré-selecionada, já
  cumpre esse papel.
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/features/agents/
├── api/
│   ├── agentsApi.ts            # + replaceAgentDelegations
│   ├── useAgents.ts            # + useReplaceAgentDelegationsMutation
│   └── useAgents.test.ts       # + casos do novo hook
├── components/
│   ├── AgentDelegationsSection.tsx        # novo — presentational, recebe
│   │                                       # agent e agentsCatalog via prop
│   ├── AgentDelegationsSection.test.tsx   # novo
│   ├── AgentDetailCard.tsx                # inalterado
│   └── ...
├── pages/
│   ├── AgentDetailPage.tsx     # chama useAgentsQuery(), renderiza
│   │                            # <AgentDelegationsSection> repassando o
│   │                            # catálogo como prop (Decision 6)
│   └── AgentDetailPage.test.tsx # + verificação de que a seção aparece
└── types/
    └── agent.ts                # + delegatesTo em Agent, + AgentSummaryReference
```

Nenhum arquivo novo fora de `features/agents/`; nenhuma pasta nova além
dos arquivos listados.

## Decisions

### Decision 1: Seção sempre editável, sem alternância leitura/edição

A seção nova é sempre editável — sem um modo leitura separado que
precise ser alternado para uma tela de edição. Ela nasce pré-selecionada
com `agent.delegatesTo` (o estado atual já é visível na própria seleção
do `MultiSelect`) e tem botão "Salvar delegações" próprio. O clique em
"Salvar" já é a confirmação — mesmo raciocínio que já dispensa modal de
confirmação em `AgentMcpServersPage` (lá, apenas a desativação de agente
tem consequência distinta o bastante — mensagens futuras rejeitadas —
para justificar um modal).

Consequência: nenhuma linha de resumo textual "Delegações: X, Y" é
adicionada a `AgentDetailCard` (diferente do padrão de
"Servidores MCP vinculados: ..." que existe lá) — seria informação
duplicada, já que a seleção do `MultiSelect` é, ela mesma, o display.

**Alternativa descartada**: toggle mostrar/ocultar edição (like a
página de MCP faz ao navegar para uma rota separada). Rejeitada porque
introduziria um padrão de interação (edição opcional dentro de conteúdo
de leitura) que não existe em nenhum outro lugar do app, sem necessidade
real — não há descoberta ao vivo nem sub-seleção por item que
justifique isolar a edição em outro estado ou rota.

### Decision 2: Exclusão do próprio agente das opções, client-side

O `MultiSelect` nunca lista o próprio agente como opção — filtrado a
partir do catálogo recebido via prop (Decision 6) antes de montar as
opções. Isso espelha,
no cliente, a mesma regra que o servidor já aplica (`PUT
/agents/{id}/delegations` rejeita com 400 quando `targetAgentIds`
contém o próprio `id` — auto-delegação). O princípio de cliente
espelhar uma regra de negócio do servidor já é usado no projeto (ex.:
`AgentForm` valida campos obrigatórios no cliente que o servidor também
valida) — aqui o mecanismo é filtragem de opção de lista, não validação
de formulário, mas o princípio (evitar uma chamada que o servidor vai
rejeitar de qualquer forma) é o mesmo.

**Alternativa descartada**: deixar o próprio agente aparecer como opção
e depender só do erro 400 do servidor para bloquear. Rejeitada porque
oferece uma opção sempre inválida sem necessidade — pior experiência
sem ganho algum, já que o catálogo completo sempre inclui o próprio
agente e é trivial filtrar por id.

### Decision 3: Agentes inativos aparecem como opção selecionável, com indicador

O catálogo de opções (recebido via prop pela seção — ver Decision 6)
inclui agentes inativos, e o `MultiSelect` os lista normalmente, com
sufixo `" (inativo)"` no rótulo
— mesmo padrão visual já usado para `McpServer` inativo em
`AgentMcpServerRow.tsx` (badge "Inativo"). O backend já permite delegar
para um agente Target inativo (não rejeita por causa do estado
`isActive`), então a interface não deve impedir isso.

O indicador de inatividade vem do catálogo completo (que tem
`isActive`), nunca de `agent.delegatesTo` — o `AgentSummaryResponse`
que compõe `delegatesTo` só tem `{ id, name }`, sem `isActive`. Isso
não é uma limitação: a lista de opções do `MultiSelect` já precisa vir
do catálogo completo de qualquer forma (é de lá que vêm os agentes
ainda não delegados), então `delegatesTo` só precisa fornecer o
conjunto inicial de ids selecionados, nunca o rótulo.

### Decision 4: Nenhuma detecção nem aviso de ciclo na interface

A interface permite registrar um ciclo indireto (A→B→C→A) ou um par
bidirecional (A→B e B→A) sem qualquer aviso — mesma postura que o
backend já adota deliberadamente no cadastro (proteção contra ciclo
existe apenas em runtime, via profundidade, em `apps/workers`; fora do
escopo desta capability de cadastro).

**Alternativa descartada**: buscar o grafo completo de delegações de
todos os agentes no cliente e validar ciclo antes do submit. Rejeitada
por complexidade real (exigiria buscar `delegatesTo` de todo o catálogo,
não só do agente atual, e rodar detecção de ciclo no cliente) para
proteger contra um caso que já tem rede de segurança em outra camada
(runtime, por profundidade). Não é uma omissão silenciosa — é uma
escolha registrada aqui porque o cadastro backend já tomou a mesma
decisão e este design a herda conscientemente.

### Decision 5: Erro de submit tratado como notificação genérica

Ao contrário de `AgentMcpServersPage` (que trata especificamente um 502
de handshake, identificando o servidor MCP que falhou), o submit de
delegações usa apenas uma notificação de erro genérica (toast), sem a
infraestrutura de erro por campo que a complexidade de MCP exigiu.

Justificativa: os únicos erros que `PUT /agents/{id}/delegations` pode
retornar são 400 (auto-delegação ou id inválido) e 404 (agente Source
não encontrado). Auto-delegação já é impossível de disparar pela
interface (Decision 2); ids inválidos são improváveis na prática porque
as opções vêm do catálogo ao vivo (`useAgentsQuery()`) no momento do
carregamento da página — só ocorreriam numa janela de corrida real
(outro agente excluído entre o carregamento da página e o submit). Não
existe, aqui, equivalente ao 502 de handshake atômico da página de MCP
(que depende de validar contra servidores MCP externos em tempo real).

**Alternativa descartada**: reaproveitar a mesma infraestrutura de erro
por campo (`submitError` com título/detalhe) da página de MCP.
Rejeitada por ser complexidade desproporcional a um caso residual — o
motivo que justificou aquela infraestrutura (identificar qual servidor
MCP específico falhou o handshake) não existe aqui.

### Decision 6: AgentDetailPage busca o catálogo, AgentDelegationsSection recebe como prop

`AgentDetailPage` chama `useAgentsQuery()` e repassa o resultado como
prop (`agentsCatalog` ou nome equivalente) para `AgentDelegationsSection`.
O componente em si nunca importa `useAgentsQuery` nem nenhum outro hook
de query/mutation — permanece presentational, recebendo `agent` e o
catálogo via props e expondo callbacks/estado local para seleção, salvar
e cancelar.

Isso segue o princípio já estabelecido no projeto: componente
apresentacional não busca dados, a página busca e repassa como prop —
a mesma resolução já usada quando `GET /providers` precisou alimentar
`AgentForm` (`AgentCreatePage`/`AgentEditPage` chamam `useProvidersQuery()`
e passam `providers` como prop; `AgentForm` não importa `useProvidersQuery`).
`AgentDelegationsSection` segue o mesmo desenho: fica testável isoladamente
com dados injetados via prop, sem precisar mockar `useAgentsQuery` no teste
do componente (só no teste de `AgentDetailPage`, que já mocka `getAgent`
hoje).

Consequência prática: a página passa a fazer duas queries (`useAgentQuery`
e `useAgentsQuery`) e trata o estado de carregamento/erro de ambas antes
de renderizar a seção — mesmo padrão já usado em `AgentMcpServersPage`,
que combina `useAgentQuery` e `useMcpServersQuery`.

**Alternativa descartada**: `AgentDelegationsSection` chama
`useAgentsQuery()` internamente. Rejeitada por contradizer o padrão já
fixado no projeto (componente apresentacional nunca importa hook de
query/mutation) e por tornar o componente mais difícil de testar
isoladamente, exigindo mock de `useAgentsQuery` em todo teste do
componente em vez de apenas passar props.

## Risks / Trade-offs

- [Risco] Corrida entre carregar a página e o submit: um agente listado
  como opção é excluído por outro usuário antes do "Salvar" →
  [Mitigação] coberto pela notificação genérica de erro (Decision 5); a
  seleção local não é perdida, o usuário pode remover a opção inválida
  e tentar novamente.
- [Risco] `useAgentsQuery()` sem paginação: se o catálogo de agentes
  crescer muito, o `MultiSelect` pode ficar pesado → [Mitigação] fora de
  escopo desta change — `AgentListPage` já usa a mesma fonte sem
  paginação hoje; se isso virar problema, é uma mudança independente
  que afeta ambas as telas.

## Open Questions

(nenhuma)
