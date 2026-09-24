## Why

A etapa 3 da linha `metricas-de-operacao` tem **duas** rotas, e só a primeira
existe. A change A (`rotas-de-agregacao-sistema`) entregou `GET /insights/system`,
aplicada, deployada e verificada em produção em 23/09/2026 — com ela vieram o
fuso de `apps/api`, a janela, o balde local, a preservação de nulo e o mapa de
regimes, todos fechados como **um contrato só**. A tela por agente das etapas 4 e
5 não tem de onde ler.

E ela **não** lê a rota do sistema filtrada por agente. A D16 da
`metricas-execucao-coleta` fixou que os dois cards de delegação da tela do agente
leem **fontes diferentes** — "Delega para" lê `delegation_outcomes` (o que o
agente *tentou*), "Acionado por" lê `task_executions` com `Origin = Delegation`
agrupado por `SourceAgentId` (o que de fato *rodou* nele). São perguntas
diferentes sobre a mesma relação, e com `NotStarted` ou `Expired` no meio os dois
lados mostram **números diferentes**. Uma rota que fosse a do sistema com um
`where` a mais não teria como produzir os dois.

Agora, porque o contrato herdado está fixado e verificado: reabri-lo depois da
tela custaria mudar as duas pontas.

## What Changes

- **Uma rota nova em `apps/api`: `GET /insights/agents/{id}`**, com a mesma
  janela, o mesmo balde e o mesmo mapa de regimes da rota do sistema — herdados
  **literalmente**, reusando `InsightsPeriod` e `MetricsOptions`, sem redecidir
  nada.
- **Os dois lados da delegação, assimétricos por construção.** "Delega para" sai
  de `delegation_outcomes` por `SourceAgentId`; "Acionado por" sai de
  `task_executions` com `Origin = 'Delegation'` e `AgentId` do agente, agrupado
  por `SourceAgentId`. A resposta **não** os apresenta como espelho, e há guarda
  que **afirma a divergência** — é ele que impede alguém "consertar" para bater.
- **O mapa das métricas refeito por escopo de agente**, contra as seis tabelas,
  dizendo por métrica se é a mesma consulta com filtro, consulta diferente, ou se
  **não existe** neste escopo. O mapa das 27 da change A é do escopo do sistema e
  não transfere por analogia.
- **Agente inexistente responde `404`**, nunca `200` com agregado vazio — um
  agregado zerado para um id que não existe é a mesma família do `0` onde deveria
  haver ausência. **Agente inativo responde `200`**: ele existe, e a medição
  aconteceu.
- **Parcialidades próprias do escopo por agente declaradas**, e são duas novas
  além das herdadas: os tokens de embedding de **indexação** e as falhas de
  indexação não têm agente em fonte alguma.

**Nenhuma tela** (etapas 4 e 5), **nenhuma migração**, **nenhum índice**,
**nenhuma mudança na rota do sistema**, e **nenhuma edição do `nginx.conf`** — o
prefixo `insights` já roteia, com cenário de spec afirmando `/insights/agents/{id}`
explicitamente.

## Capabilities

### New Capabilities

- `agent-insights-aggregation`: a rota de leitura `GET /insights/agents/{id}` de
  `apps/api` — o recorte por agente do catálogo de métricas, a assimetria entre os
  dois lados da delegação, o que o escopo por agente **não** sustenta, e o `404`
  para agente inexistente. Herda de `system-insights-aggregation` a janela, o
  balde local, a preservação de nulo e o mapa de regimes, sem reabri-los.

### Modified Capabilities

Nenhuma. `system-insights-aggregation` já declara que **não cobre** o escopo por
agente e que ele "não é esta rota filtrada — é outro conjunto de consultas"; esta
change cumpre aquela declaração em vez de alterá-la. `api-system-timezone` e
`route-authentication` são consumidas como estão. `server-deployment` já tem o
cenário *"Prefixo de insights cobre as rotas de escopo abaixo dele"*, que cita
`/insights/agents/{id}` pelo nome.

## Impact

**`apps/api` — o único app tocado.**

- **Criados:** o endpoint, a query e o handler de `GetAgentInsights`, o DTO do
  agregado por agente, e o arquivo de teste com o seu fixture.
- **Modificados:** `Program.cs` (nada — a rota entra pelo `MapInsightsEndpoints`
  já registrado), `InsightsEndpoints.cs` (uma rota a mais no mesmo mapeamento).
- **Reusado sem alteração:** `InsightsPeriod` — que a change A **extraiu com este
  consumidor declarado no comentário** —, `MetricsOptions`, a checagem de boot do
  fuso e `InsightsCaveats`.

**Tabelas lidas, todas somente leitura:** `task_executions`, `provider_calls`,
`delegation_outcomes`, `embedding_calls`, `knowledge_indexing_attempts`,
`a2a_tasks` e `agents` (esta última só para decidir o `404`).

**Custo de suíte nomeado:** `apps/api` tem hoje **30 classes de teste que sobem
contêiner** (26 por `IClassFixture` e 4 que constroem o seu direto, lido em
23/09/2026); esta change faz a **31ª**. É a condição observável que a convenção 22
manda recalibrar na change que a produz, e não descobrir na seguinte.

**Não tocados, e verificado:** `apps/frontend/deploy/nginx.conf` (o prefixo já
cobre), `apps/workers`, `apps/inbox`, `docker-compose*.yml`, `.env.example`.
