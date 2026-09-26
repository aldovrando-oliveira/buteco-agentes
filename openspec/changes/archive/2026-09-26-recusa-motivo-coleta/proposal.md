**Issue:** #51 · **Depende de:** #52 (etapa 4, mergeada em `5f2f6f8`)

## Why

Três causas distintas de recusa de `apps/api` colapsam num único estado
`rejected`, e **não há coluna de motivo em lugar algum** — reconferido no código
atual, não herdado do registro: `EnqueueingAgentHandler.cs:29` (agente inativo),
`:38` (`Provider`/`Model` nulos) e `:46` (provedor não configurado no ambiente).
É a métrica **M29** do catálogo, a única das 27 servidas pelas duas rotas de
agregação que não tem fonte nenhuma, e é o `caveat`
`rejection-reason-not-collected` que as duas declaram hoje.

A ordem desta change foi **invertida de propósito** pelo dono: a #51 dizia "vem
antes da #52", e a tela veio primeiro porque **ela é o primeiro consumidor real
do contrato**. A coleta nasce agora sabendo a cardinalidade, o vocabulário e a
anulabilidade que o card de Motivos pediu, em vez de escolhê-los por suposição.
O que o protótipo e o componente pedem está lido e registrado no `design.md`
(D1), inclusive uma divergência: o protótipo **nomeia uma** das três causas com
contagem (`"Agente sem provider ou modelo configurado — 5"`), a tela **não nomeia
nenhuma**, e a tela ganha.

## What Changes

**App afetado: `apps/api`, e só ele.**

- **Tabela nova `task_rejections`** (`apps/api`), com `TaskId`, `AgentId`,
  `Reason` e `RejectedAt` — **não** uma coluna em `a2a_tasks` e **não** uma linha
  em `task_executions`. As três saídas foram medidas contra o código; a decisão e
  o custo de cada uma estão na D2, e o precedente que a fecha já está escrito na
  base (`AppDbContext.cs:361-376`).
- **Vocabulário fechado, gravado como texto, nunca ordinal**, determinado pela
  **causa no código** e nunca por texto de mensagem — molde de
  `ExecutionMetricsValues.FailurePhase`.
- **Quatro valores, não três** — e esse é um achado desta change, não um
  alargamento de escopo: o sítio `:29` carrega **duas** causas, porque
  `GetAgentStateAsync` devolve `default` quando o agente não existe e isso entra
  no mesmo `if (!agentState.IsActive)`. Gravar `AgentInactive` para agente
  inexistente afirmaria um estado que ninguém leu (convenção 13). A D4 traz a
  alternativa de ficar em três, se o dono preferir.
- **As duas rotas passam a servir o motivo** (`apps/api`): um campo de contagem
  próprio para a recusa de entrada e uma lista de motivos com contagem, no
  **mesmo formato** que o card de Motivos já consome para as fases de falha —
  rótulo e contagem, lista rasa, sem subcardinalidade.
- **Terceiro regime de medição, `rejection`** — o mapa de regimes foi feito mapa
  exatamente para isto (`system-insights-aggregation`: *"a etapa seguinte da
  linha acrescentará um terceiro regime"*), e sem ele a contagem de motivos de um
  período anterior à coleta viria incompleta em silêncio.
- **Checagem de boot de que todo regime declarado pelas rotas tem instante em
  configuração** (convenção 8). Sem ela, uma chave ausente no mapa desliga o
  recorte de regime e a rota emite número plausível sobre período não medido —
  o modo de falha que o próprio `appsettings.json` já descreve em comentário, e
  que comentário não reprova.
- **`rejection-reason-not-collected` sai das duas respostas** — o motivo passa a
  ter fonte. **`rejections-missing-from-executions` fica**, com o texto
  inalterado: a recusa de entrada continua sem linha de execução, continua fora
  do percentual de falha, e M28 (falhas por agente/provedor/modelo) continua
  parcial **por construção**, porque duas das três causas não têm provedor nem
  modelo para agrupar. A D6 decide as duas, com o motivo.
- **`rejectedCount` não muda de conteúdo nem de fonte**, e passa a ser
  documentado pelo que ele de fato conta. Achado medido: hoje ele conta **só**
  `DelegationDepthExceeded` de `apps/workers` (único sítio que grava
  `TerminalState = 'Rejected'`), enquanto a tela o rotula *"Recusadas na
  entrada"* com o texto das causas de `apps/api` — nenhuma das quais está nesse
  número. A change **acrescenta** o campo certo em vez de repropósito silencioso
  de um existente (D5).

**Nada de UI aqui** (convenção 1): consumir o dado novo é change de seguimento da
#52, com issue própria aberta ao abrir esta.

## Capabilities

### New Capabilities

- `agent-rejection-metrics`: a coleta, por `apps/api`, do motivo de toda recusa
  feita antes de a task ser publicada — o vocabulário fechado gravado como
  texto, a linha por task recusada, a anulabilidade, e a garantia de que a
  gravação da métrica nunca altera o que a task terminou sendo.

### Modified Capabilities

- `system-insights-aggregation`: a rota do sistema passa a servir a contagem de
  recusas de entrada e os motivos delas, declara o terceiro regime, e a lista de
  parcialidades conhecidas perde a do motivo e mantém a da linha de execução
  com o escopo corrigido.
- `agent-insights-aggregation`: o mesmo, no escopo do agente — e é aqui que o
  gatilho pendurado pela `rotas-de-agregacao-agente` é cumprido
  (*"decidir para os dois escopos ao mesmo tempo"*), para que os dois não meçam
  coisas diferentes com o mesmo rótulo.

**Deliberadamente NÃO modificada: `a2a-task-lifecycle`.** As três recusas
continuam fazendo exatamente o que a spec exige — transicionar para `rejected`
sem publicar job, sem código HTTP próprio. A coleta é capability separada pelo
mesmo motivo que `agent-execution-metrics` é separada do ciclo que ela observa.

## Impact

- **`apps/api`, código novo:** entidade `TaskRejection` e o escritor dela,
  mapeamento no `AppDbContext`, uma migração, a constante do regime, a checagem
  de boot, os campos no `SystemInsightsResponse`/`AgentInsightsResponse` e uma
  consulta nova em cada um dos dois handlers de agregação.
- **`apps/api`, código modificado:** `EnqueueingAgentHandler` (três sítios de
  recusa mais a leitura de estado do agente), os dois handlers de agregação e o
  `appsettings.json` (a terceira chave do mapa de regimes).
- **Contrato das duas rotas:** aditivo em campo, **subtrativo em `caveat`**. Um
  cliente que hoje renderiza `rejection-reason-not-collected` deixa de recebê-lo;
  o único cliente é `apps/frontend`, que o classifica como `not-on-this-page` e
  não o renderiza — conferido em `caveatLabels.ts:60-77`.
- **Não afetados, e a razão:** `apps/workers` (não participa da recusa de
  entrada, e a tabela nova não é espelhada porque ninguém lá a lê),
  `apps/inbox` (cliente A2A, não muda de contrato: o estado devolvido continua
  `rejected`), `apps/frontend` (change de seguimento, issue própria), `libs/`
  (nada a compartilhar — o vocabulário só existe em `apps/api`) e
  `stack/nginx.conf`.
- **Sem dependência nova de pacote.** Nenhuma versão de runtime, framework ou
  biblioteca é escolhida ou alterada por esta change.
- **Verificação:** `Buteco.Api.Tests` com Testcontainers — a tabela nova, o
  vocabulário como texto no banco, os quatro caminhos de recusa e as duas rotas.
  O guarda de cada causa precisa reprovar contra `HEAD` (convenção 15), e a
  mutação é quebrar a atribuição de **uma** causa e ver **só** o guarda dela
  reprovar.
