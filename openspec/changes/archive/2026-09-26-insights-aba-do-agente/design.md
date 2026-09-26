## Context

**Etapa 5 e última da linha `metricas-de-operacao`.** A etapa 1 coletou, a 2
preservou o nulo ponta a ponta, a 3 agregou em duas rotas — a do sistema (change
A) e a do agente (change B) — e a 4 entregou a página do sistema. Falta a
superfície do escopo do agente.

**A rota já está em produção e não é tocada aqui.** `GET /insights/agents/{id}`
foi mergeada na etapa 3, change B (`rotas-de-agregacao-agente`, 23/09), e a
capability `agent-insights-aggregation` já fixa o contrato. Esta change é
`apps/frontend` e nada mais.

### O que foi lido, e quando

**Os protótipos, primeiro — tarefa 0, não conferência posterior.** A #52 precisou
de **treze rodadas** de conferência manual, e nenhum dos achados do dono foi de
lógica: todos de fidelidade ao protótipo, de registro ou de contraste. A leitura
prévia não evita sozinha, porque o desenho tem decisões que só aparecem quando o
componente é montado — mas trabalhar do `02` ou de `design.md` arquivado garante
a rodada extra.

Lidos em 26/09/2026, pela ferramenta `Artifact` (o MCP `claude-design` recusa a
conexão com `FIRST_PARTY_AUTH_REJECTED`), do canvas
`https://claude.ai/artifact/R9JSCiYw7pBEognkKhiaDh`, versão `1790386152-b562`:

| arquivo | o que traz |
|---|---|
| `project/canvas.json` | as três notas do autor — `dados`, `cenarios`, `estados` |
| `project/Agente-Insights.dc.html` | cenário 1 — só delega |
| `project/Agente-Delegado.dc.html` | cenário 2 — só é delegado |
| `project/Agente-Misto.dc.html` | cenário 3 — delega e é delegado |
| `project/Estados.dc.html` | a gramática e os seis quadros de exceção |
| `project/Insights-Claro.dc.html` | a página do **sistema** no tema claro |

**Duas coisas só existem no `canvas.json`, e nenhum registro as tem:**

- a nota `cenarios` fixa o **quarto cenário**, que não tem artboard: agente que
  não delega nem é delegado é *"o mesmo card com as duas seções tracejadas"*.
  Quem ler só a lista de arquivos não o encontra;
- a nota `estados` dá a autoridade da regra de divergência, com as palavras do
  autor: *"contrariar o protótipo é resultado legítimo e vira registro, com
  gatilho para voltar — não implementação silenciosa"*. É a convenção 17 dita
  pelo dono dentro do próprio desenho.

**O deslize registrado, conferido:** o quadro 4 do `Estados.dc.html` se intitula
*"A gramática dos três estados"* e desenha **quatro** linhas — medido, célula
vazia, travessão e zero. O desenho manda; o título está errado, e
`utils/metricState.ts` já implementa os quatro.

**O `Insights-Claro.dc.html` não é desta change.** Ele desenha a página do
sistema no tema claro, com o mapa de calor. **Os três artboards do agente não têm
mapa de calor nem série diária** — logo a inversão de escala entre esquemas, já
contratada em `frontend-visual-theme` desde a #52, não é exercitada por esta aba,
e `PeriodHeatmapCard` e `DailyTasksCard` não entram nela.

**O contrato da rota**, lido em `apps/api/src/Buteco.Api/Insights/Responses/AgentInsightsResponse.cs`
e nos handlers, não de memória nem por analogia com o gêmeo do sistema
(convenção 6). O que o tipo do agente **não tem** é parte do contrato: não há
`ByAgent` e não há `IndexingFailures`, e a ausência é a primeira barreira — lista
vazia é o texto de *"medi e não achei nada"*.

## Goals / Non-Goals

**Goals:**

- A aba Insights no detalhe do agente, montada dos artboards, cobrindo os
  **quatro** cenários de delegação.
- A assimetria dos dois lados da delegação legível na tela, com guarda que
  **afirma a divergência**.
- `404`, `200` zerado e agente inativo distinguíveis entre si e do travessão.
- Os rótulos das métricas que mudam de significado reescritos para este escopo.
- Os `caveats` renderizados ao lado do número que cada um limita **nesta**
  superfície.
- As divergências com o protótipo registradas, com causa e gatilho.
- Deixar a #67 em condição de ser decidida, **sem decidi-la**.

**Non-Goals:**

- **Não tocar `apps/api`.** A rota serve tudo deste escopo; o que ela não serve é
  achado a sequenciar (convenção 1), nunca backend de improviso dentro da change
  de tela.
- **Não decidir a #67**, nem implementar a #66 ou a #75.
- **Não corrigir a página do sistema.** Os dois achados do `proposal.md` — os
  campos da #51 que não chegaram ao painel e a entrada morta no mapa de
  `caveats` — viram issue. A **única** exceção é a entrada morta, que cai por
  tabela ao reescrever o mesmo arquivo, e mesmo assim em tarefa própria que o
  dono pode descartar sem afetar o resto.
- Não tocar `apps/workers`, `apps/inbox`, `libs/`, `nginx.conf` nem
  `src/app/routes.tsx`.
- Não introduzir biblioteca de gráficos.
- Não persistir o período na URL. É item aberto herdado da #52, com gatilho no
  primeiro pedido de compartilhar link.

## A árvore de pastas proposta

Tudo em `apps/frontend`. `C` = criado, `M` = modificado, o resto é contexto
intocado.

```
apps/frontend/src/
├── features/
│   ├── agents/
│   │   └── pages/
│   │       ├── AgentDetailPage.tsx                     M  quinta aba
│   │       └── AgentDetailPage.test.tsx                M  casos da quinta aba
│   └── insights/
│       ├── api/
│       │   ├── insightsApi.ts                          M  getAgentInsights
│       │   ├── insightsApi.test.ts                     M  URL e 404
│       │   ├── useSystemInsights.ts                       intocado
│       │   ├── useAgentInsights.ts                     C  chave + janela no queryFn
│       │   └── useAgentInsights.test.tsx               C
│       ├── components/
│       │   ├── AgentInsightsTab.tsx                    C  período, consulta, composição
│       │   ├── AgentInsightsTab.test.tsx               C
│       │   ├── AgentKpiGrid.tsx                        C  os quatro KPIs do artboard
│       │   ├── AgentKpiGrid.test.tsx                   C
│       │   ├── TimeBreakdownCard.tsx                   C  "Onde o tempo foi"
│       │   ├── TimeBreakdownCard.test.tsx              C
│       │   ├── TaskDurationCard.tsx                    C  "Duração da task"
│       │   ├── TaskDurationCard.test.tsx               C
│       │   ├── AgentModelsCard.tsx                     C  "Modelos que este agente usou"
│       │   ├── AgentModelsCard.test.tsx                C
│       │   ├── AgentDelegationCard.tsx                 C  as duas seções
│       │   ├── AgentDelegationCard.test.tsx            C
│       │   ├── AgentFailuresCard.tsx                   C  falhas + as duas recusas
│       │   ├── AgentFailuresCard.test.tsx              C
│       │   ├── KpiCard.tsx                             C  extraído (2 consumidores)
│       │   ├── InsightsKpiGrid.tsx                     M  passa a importar KpiCard
│       │   ├── WeekdayActivityCard.tsx                 M  nota de rodapé opcional
│       │   ├── WeekdayActivityCard.test.tsx            M  caso da nota
│       │   ├── MetricValue.tsx                            reusado sem mudança
│       │   ├── DeclaredGap.tsx                            reusado sem mudança
│       │   ├── PeriodPicker.tsx                           reusado sem mudança
│       │   ├── PartialMeasurementNotice.tsx               reusado sem mudança
│       │   ├── DailyTasksCard.tsx                         NÃO usado nesta aba
│       │   ├── PeriodHeatmapCard.tsx                      NÃO usado nesta aba
│       │   ├── AgentConsumptionCard.tsx                   só na página do sistema
│       │   ├── ProviderConsumptionCard.tsx                só na página do sistema
│       │   ├── ConversationModelsCard.tsx                 só na página do sistema
│       │   ├── FailuresCard.tsx                           só na página do sistema
│       │   ├── FailureReasonsCard.tsx                     só na página do sistema
│       │   └── NonTerminalBanner.tsx                      só na página do sistema
│       ├── pages/
│       │   └── SystemInsightsPage.tsx                     intocada
│       ├── test/
│       │   ├── systemInsightsFixture.ts                   intocada
│       │   └── agentInsightsFixture.ts                 C  o duplo desta change
│       ├── types/
│       │   ├── systemInsights.ts                          intocada (ver Achados)
│       │   └── agentInsights.ts                        C  o contrato do escopo
│       └── utils/
│           ├── caveatLabels.ts                         M  2 códigos + posição por superfície
│           ├── caveatLabels.test.ts                    M
│           ├── delegationRows.ts                       C  o cruzamento dos dois lados
│           ├── delegationRows.test.ts                  C
│           ├── delegationOutcomeLabels.ts              C  os quatro resultados
│           ├── delegationOutcomeLabels.test.ts         C
│           ├── rejectionReasonLabels.ts                C  os quatro motivos
│           ├── rejectionReasonLabels.test.ts           C
│           ├── metricState.ts                             reusado sem mudança
│           ├── measuredDays.ts                            reusado sem mudança
│           ├── insightsWindow.ts                          reusado sem mudança
│           ├── failurePhaseLabels.ts                      reusado sem mudança
│           └── heatScale.ts                               NÃO usado nesta aba
```

**Contagem que a projeção da convenção 18 usa, lida desta árvore, não de
memória:** 13 criados de produção, 13 criados de teste (um deles é o duplo),
5 modificados de produção, 5 modificados de teste.

## O mapa do artboard para o campo da resposta

Montado elemento a elemento, contra o artboard e contra
`AgentInsightsResponse.cs`. **É este mapa que decide o que diverge**, e ele é o
trabalho de que as sete divergências abaixo saem.

| elemento do artboard | campo da rota | estado |
|---|---|---|
| "Tasks executadas" / número | `volume.executedTaskCount` (`int`) | direto |
| ↳ subtítulo, origem externa | `volume.externalOriginTaskCount` | direto |
| ↳ subtítulo, por delegação | `volume.delegationOriginTaskCount` | direto |
| "Tokens de conversa" / número | `sumKnown(tokens.conversation.inputTokens, .outputTokens)` | direto |
| ↳ subtítulo, entrada e saída | os dois campos, cada um com o seu estado | direto |
| "Tokens por task" / número | `tokens.perTask.average` (`double?`) | direto |
| ↳ subtítulo, p95 | `tokens.perTask.p95` | direto |
| "Taxa de falha" / número | `ratio(errors.failedCount, volume.executedTaskCount)` | direto |
| ↳ subtítulo, N falhas | `errors.failedCount` | direto |
| ↳ subtítulo, "nenhuma recusa" | `errors.rejectedCount` | **D6** |
| "Onde o tempo foi" / barra empilhada | — | **D5, não desenhada** |
| ↳ Fila | `performance.queueTime.averageMs` | direto |
| ↳ Chamadas ao provedor | `performance.providerCallDuration.averageMs` | **D5** — é por chamada |
| ↳ "Ferramentas" | `performance.nonProviderResidual.averageMs` | **D4** — rótulo |
| ↳ cabeçalho "mediana por task" | `performance.taskDuration.averageMs` | **D3** — é média |
| "Duração da task" / "Mediana" | `performance.taskDuration.averageMs` | **D3** — é média |
| ↳ p95 | `performance.taskDuration.p95Ms` | direto |
| ↳ "Chamadas por task" | `performance.providerCallsPerTask` | direto |
| "Modelos…" / Modelo, Provedor | `tokens.byModel[].model`, `.provider` | direto |
| ↳ Chamadas | `tokens.byModel[].callCount` (`int`) | direto |
| ↳ Tokens | `tokens.byModel[].totalTokens` (`long?`) | direto — é a célula vazia |
| ↳ "Cache lido" | — | **D7** — L3, #66 |
| ↳ nota "o modelo mudou no período" | a própria cardinalidade de `byModel` | direto |
| ↳ nota "N são compactação" | — | **D7** — L2, #66 |
| "Dias da semana" / sete linhas | `temporal.byWeekday` + cobertura da série | direto |
| ↳ nota de rodapé por cenário | `volume.externalOrigin` × `.delegationOrigin` | **D8** |
| "Delegação" / "Delega para" | `delegation.delegatesTo[]` × catálogo | **D1, D2** |
| "Delegação" / "Acionado por" | `delegation.triggeredBy[]` × catálogo | **D1, D2** |
| "Por que as N falhas…" / tabela | `errors.byPhase` + `errors.byProviderAndModel` | **D9** |
| (sem elemento) | `errors.rejectedAtEntryCount`, `.rejectionsByReason` | **D6** |
| (sem elemento) | `tokens.searchEmbeddingInputTokens`, `tokens.byProvider` | **D10** |
| (sem elemento) | `performance.maxDepthAtWhichAgentRan` | **D10** |
| (sem elemento) | `errors.nonTerminal` | **D10** |

## Decisions

### D1 — Os dois lados da delegação são dois conjuntos, e a tela afirma a divergência

**Decisão:** o card Delegação tem duas seções que **nunca** são reconciliadas.
Nenhum total soma os dois lados, nenhuma diferença é sinalizada como alerta, e o
texto de `delegation-sides-are-not-mirrors` fica **dentro do card**, ao lado dos
dois conjuntos que ele limita.

**Por que é a decisão central desta change.** A rota lê `delegatesTo` de
`delegation_outcomes` pelo agente de **origem** e `triggeredBy` de
`task_executions` pelo agente de **destino**. Duas causas independentes fazem os
números discordarem para a mesma relação entre os mesmos dois agentes:

1. **resultado que não produz execução** — `NotStarted` nunca cria task no
   destino, e `Expired` pode ter criado uma que nunca rodou;
2. **dois relógios** — `delegation_outcomes` não tem coluna temporal utilizável
   (`LastObservedAt` é a última leitura, não o instante do evento), então a
   janela do lado de quem delega é emprestada da execução **de origem**, e a do
   outro lado é a própria linha de destino. Uma delegação às 23:58 cujo destino
   roda às 00:03 cai em **dias diferentes** do balde.

**A segunda causa não some com período maior** — é estrutural, não ruído de
borda. Está escrita assim no `02`, e é a frase que a etapa 5 precisava receber.

**Medido em produção hoje os números BATEM** — 21 `Completed` + 2 `Expired` = 23
execuções — porque a janela não tem nenhum `NotStarted` nem nada na fronteira.
**Bater é possível, não é garantido**, e a tela não pode ser escrita supondo que
batem. O guarda que protege isto exercita um cenário em que discordam e **afirma
a discordância**: um guarda que afirmasse igualdade reprovaria o comportamento
correto (convenção 15).

**Alternativas recusadas:**

- *Apresentar um lado só, ou uma seção "Delegações" unificada.* É o defeito que a
  change B existe para impedir, e o `AgentDelegationInsightsResponse` diz isso
  por escrito: *"quem desenhar a tela não pode fazê-los parecerem espelho"*.
- *Sinalizar a diferença como aviso.* Transforma resultado correto em defeito
  aparente. A declaração é o `caveat`, que explica; um alerta acusa.
- *Escolher o menor dos dois como "o número verdadeiro".* Inventa uma terceira
  medição que nenhuma fonte fez.

### D2 — "Delega para" mostra o resultado discriminado, e é divergência com o protótipo

**Decisão:** cada linha de destino traz a contagem **total** e, abaixo dela, os
resultados nomeados com as suas contagens. A barra e a contagem da direita — a
forma do artboard — ficam; o que entra é a linha de detalhe.

**Por quê:** `DelegatesToResponse` é `(TargetAgentId, Outcome, Count)` — a rota
devolve **uma linha por par destino × resultado**. Colapsar num total por destino
joga fora exatamente o que explica a divergência da D1: sem ver que houve
`NotStarted`, a diferença entre os dois lados fica sem causa na tela, e o `caveat`
vira prosa que ninguém liga a número nenhum.

**É acréscimo ao artboard**, que desenha uma barra por destino e nada mais.
Registrado como divergência (convenção 9), com a razão: o desenho foi feito
antes de a rota discriminar o resultado, e a discriminação é o que torna o
`caveat` legível.

**Alternativa recusada:** *uma linha por par destino × resultado.* Multiplica as
linhas por quatro no pior caso e desmancha a leitura de ranking que o artboard
procura — "para quem este agente mais delega" deixa de ser lido de cima para
baixo.

### D3 — "Mediana" vira "Média", porque a palavra é o contrato

**Decisão:** onde o artboard escreve *"Mediana"* e *"mediana por task"*, a aba
escreve **média**. O número é `avg(EndedAt − SubmittedAt)`, e não existe
`percentile_cont(0.5)` na rota.

**Por quê:** é a lacuna **L5**, já registrada na issue **#66**, e o precedente é
a D9 da #52: chamar média de mediana afirma uma propriedade que o número não
tem — quem lê "mediana" conclui que metade das tasks foi mais rápida, o que uma
média com cauda longa não diz.

**Alternativa recusada:** *calcular a mediana no cliente.* Não há como: a rota
devolve o agregado, não as durações.

### D4 — O resíduo não se chama "Ferramentas"

**Decisão:** a terceira parcela de "Onde o tempo foi" é rotulada **"Fora do
provedor"**, com o texto de `residual-is-not-only-tools` ao lado.

**Por quê:** o artboard escreve "Ferramentas", e a nota dele já diz que é o que
sobra depois de descontar as chamadas ao provedor. Mas o `caveat` da própria
rota diz o resto: o resíduo inclui espera de lock, chamadas a servidores MCP e
busca vetorial — *"e é por isso que a métrica não se chama 'tempo em tools'"*.
Rótulo que afirma mais do que o número sabe é convenção 13 furada num nome.

**Consequência na posição do `caveat`:** na página do sistema,
`residual-is-not-only-tools` está classificado `not-on-this-page`, porque lá o
resíduo não é desenhado. **Aqui ele tem número**, e por isso tem posição. É o
caso concreto que obriga a posição a ser **por superfície** (D11).

> **REVERTIDA PELO DONO na conferência manual de 26/09/2026.** O nome do
> artboard, "Ferramentas", volta. O `caveat` **não** sai junto: com o nome
> antigo de volta ele é o único aviso na tela de que o resíduo inclui espera de
> lock, MCP e busca vetorial, e passou a ser **ícone com tooltip no cabeçalho**,
> a forma que o dono escolheu para esta família de casos. A régua que a D4
> defendia continua registrada aqui e no `02`: rótulo que afirma mais do que o
> número sabe é convenção 13 furada num nome. O que mudou foi onde o aviso mora,
> não se ele existe.

### D5 — A barra empilhada de "Onde o tempo foi" não é desenhada, e isso vira issue

**Decisão:** o card mantém o título, o lugar e as três parcelas, **sem a barra
proporcional**, e o cabeçalho deixa de afirmar um total que a soma não produz.
Cada parcela aparece com o seu número, o seu estado e o seu `sampleCount`.

**Por quê — lido no SQL, não suposto.** As três parcelas do artboard **não são
composições da mesma quantidade**:

| parcela | o que a rota mede | população |
|---|---|---|
| Fila | `avg(StartedAt − SubmittedAt)` por execução | exclui `SubmittedAt` nulo |
| Chamadas ao provedor | `avg(DurationMs)` **por chamada** | todas as chamadas |
| Resíduo | `avg((EndedAt − StartedAt) − Σ chamadas)` **por task** | exclui `EndedAt` nulo |
| Total | `avg(EndedAt − SubmittedAt)` por execução | exclui `SubmittedAt` nulo |

O tempo de provedor **por task** não é servido. Derivá-lo por
`providerCallDuration.averageMs × providerCallsPerTask` é o **produto de duas
médias**, que não é a média do produto; derivá-lo por `total − fila − resíduo`
subtrai médias de **três populações diferentes**. Os dois caminhos produzem um
número plausível que não é média de coisa nenhuma — o modo de falha mais caro
desta família de telas, porque tem aparência de evidência.

Uma área desenhada **afirma uma proporção**. Desenhar a barra a partir daí seria
afirmar uma repartição do tempo que ninguém mediu, e por isso o requisito
"proporção desenhada não é composta a partir de conhecimento parcial" está na
spec com guarda próprio.

**Vira issue nova em `apps/api`**, com gatilho no primeiro pedido de ver a
repartição do tempo: a rota precisaria de `avg(Σ DurationMs por task)` e das
três parcelas sobre a **mesma** população.

**Alternativa recusada:** *desenhar a barra com os números que existem.* É o
caso que a convenção 6 nomeia como o mais difícil de pegar — fonte consultada
corretamente, número certo, respondendo a uma pergunta que não era a que
decidia.

> **REVERTIDA PELO DONO na conferência manual de 26/09/2026, com a medição na
> mesa:** *"entendo que as medições são independentes e por isso somar não
> representa a média da task, mas não tem problema"*. A barra entra, e o
> cabeçalho ganha o total — é a forma da convenção 17: contrariar o registro é
> resultado legítimo quando vira registro.
>
> **O que a implementação preserva, e não é negociação:**
>
> - o rótulo do total é **"total das três"**, e não "mediana por task" nem
>   "média por task". O número é a soma de três médias sobre populações
>   diferentes, e o rótulo diz exatamente isso — a régua da D3 continua valendo
>   para a palavra;
> - **a barra só é desenhada com as três parcelas conhecidas.** Isto NÃO é a
>   recusa da D5 disfarçada: é o requisito de spec "proporção desenhada não é
>   composta a partir de conhecimento parcial", que continua na
>   `agent-insights-ui` e continua com guarda. Uma faixa de largura zero no
>   lugar do desconhecido afirmaria que aquela etapa não levou tempo nenhum;
> - o total também fica **vazio** quando alguma parcela é nula: um "total" que
>   ignora a parcela desconhecida afirma que ela não pesou nada.
>
> A medição que motivou a recusa **continua válida e continua na issue #81** —
> ela é o que a rota precisaria para a soma ser a duração da task. O que mudou
> foi o julgamento sobre o custo de mostrar a soma enquanto isso não existe, e
> esse julgamento é do dono.

### D6 — As recusas são duas, de regimes diferentes, e não cabem no mesmo subtítulo

**Decisão:**

- o subtítulo do KPI "Taxa de falha" mantém `errors.rejectedCount` — a recusa
  **com linha de execução** —, porque é a população do mesmo regime do
  percentual que está logo acima, e é dela que o artboard fala;
- `errors.rejectedAtEntryCount` e `errors.rejectionsByReason` vão para o card de
  falhas, num grupo próprio, com o **regime de recusa** declarado no cabeçalho
  do grupo;
- os dois números **nunca** são somados.

**Por quê:** somá-los juntaria duas janelas de regime diferente num rótulo só —
é o que o XML doc de `AgentErrorInsightsResponse` proíbe por escrito. E pôr o de
entrada no subtítulo do KPI colocaria um número de outro regime ao lado de um
percentual que não o inclui, sem lugar para declarar o regime: a regra do
"medindo desde por regime" existe justamente contra isso.

**Nota de contexto que não é escopo:** este é o **primeiro** lugar do painel onde
o motivo da recusa aparece. A página do sistema ainda não conhece os campos que
a #51 entregou — é o achado do `proposal.md`, e vira issue.

> **A METADE DESTA DECISÃO QUE CRIAVA UM GRUPO NOVO FOI REVERTIDA em 26/09/2026,
> e ela contradizia a D10 desta mesma change.**
>
> A D10 diz, por escrito, que métrica servida e não desenhada **não ganha
> elemento novo** — *"criar um quadro novo acrescenta à tela um elemento cuja
> única função é falar do que ela não mostra, e o peso dele compete com os
> números que ela mostra"*. Pôr `rejectedAtEntryCount` e `rejectionsByReason`
> num grupo próprio do card de falhas foi exatamente isso, e a contradição
> passou pela redação, pela implementação e por dez guardas verdes.
>
> **Quem viu foi o dono, na tela**, e pelo sintoma que a D10 previu: o rodapé
> com o grupo, o regime e o texto do `caveat` pesava mais que a tabela de
> falhas — que é o card inteiro.
>
> A recusa de entrada passa para a lista de **servido e não desenhado**, com
> gatilho. **A contagem de `rejectedCount` continua no subtítulo do KPI**, que é
> onde o artboard a desenha, e o `caveat`
> `rejections-missing-from-executions` foi para lá com ela.
>
> **O que fica da D6:** a razão de não somar as duas recusas, que continua
> inteira e continua sendo o motivo de `rejectedCount` e `rejectedAtEntryCount`
> serem campos separados no tipo. O que caiu foi o elemento, não o raciocínio.

### D7 — As duas lacunas do card de modelos: coluna sai, nota vira lacuna declarada

**Decisão, pela regra já fixada na `system-insights-ui`:**

- **"Cache lido"** é uma **coluna** sem fonte → **sai**, sem deixar quadro no
  lugar. `ModelTokenResponse` não tem cache; o único cache do escopo é
  `tokens.conversation.cachedInputTokens`, que é o **total do agente**, e
  reparti-lo por modelo inventaria a distribuição. É a L3, issue **#66**;
- **"Das N chamadas, M são de compactação"** é um subtítulo sem fonte → vira
  **lacuna declarada inline**, no mesmo peso do subtítulo. `ProviderCall.Purpose`
  **é gravado** e a rota não o devolve — o qualificador tem de dizer **"não
  devolvida por esta rota"**, nunca "não coletada", que é o erro com o sinal
  trocado que a #52 cometeu e corrigiu. É a L2, issue **#66**.

**A demonstração da célula vazia não se perde com a saída da coluna.** Ela passa
para **Tokens**, que é `long?` na mesma tabela: um modelo cujas chamadas não
reportaram token nenhum chega nulo, e a asserção que protege isso é **negativa** —
o teste afirma a **ausência do zero**, não a presença do vazio.

### D8 — A nota do card de dias da semana sai da composição de origem, não do cadastro

**Decisão:** `WeekdayActivityCard` ganha uma prop opcional `note`, e a aba decide
o texto por `volume.externalOriginTaskCount` × `volume.delegationOriginTaskCount`:

| composição | nota |
|---|---|
| só origem externa | padrão próprio: este agente recebe os pedidos diretamente |
| só por delegação | não é acionado diretamente: o padrão é o de quem o aciona |
| as duas | parte dos pedidos chega direto e parte por delegação |
| nenhuma task | **sem nota** |

**Por quê a origem e não o cadastro:** o artboard do agente misto — que **tem**
vínculo de saída — escreve a nota de "não é acionado diretamente", porque as 97
tasks dele são todas de delegação. A nota fala do **padrão medido**, não do
cadastro. Decidir pelo cadastro daria a nota errada para um agente que tem
vínculo e não o usou.

**A linha "as duas" não tem artboard** — os três desenhados são puros. O texto é
novo, e está na lista da conferência manual.

### D9 — A tabela única de falhas vira dois grupos, porque a junção não tem fonte

**Decisão:** o card de falhas apresenta **grupos rotulados**: por fase
(`errors.byPhase`), por provedor e modelo (`errors.byProviderAndModel`) e, no
terceiro, as recusas de entrada por motivo (D6).

**Por quê:** o artboard desenha **uma** tabela com descrição, uma segunda coluna
que mistura nome de modelo com nome de servidor MCP, e a contagem. Nenhum campo
da rota junta fase a provedor/modelo, e **nenhum** campo traz servidor MCP: as
duas listas são agregações independentes sobre a mesma população, e cruzá-las no
cliente inventaria a junção. Divergência registrada, com o mesmo formato que a
página do sistema já usa.

**Sem falha no período:** o quadro tracejado do `Agente-Delegado.dc.html`, com o
total que chegou a concluído — *"Nenhuma falha no período. As 65 tasks chegaram a
concluído."* — e não tabela vazia.

### D10 — O que a rota serve e o protótipo não desenha **não ganha elemento**

**Decisão:** quatro conjuntos servidos ficam **fora** da aba, e cada um é
registrado com gatilho:

| servido | por que fica fora | gatilho para voltar |
|---|---|---|
| `tokens.searchEmbeddingInputTokens` e `tokens.byProvider` | nenhum artboard do agente tem card de embedding nem de provedor | primeiro pedido do dono |
| `performance.maxDepthAtWhichAgentRan` | sem elemento no artboard | idem — e o rótulo já está escrito abaixo |
| `errors.nonTerminal` | a aba não tem o banner que a página do sistema tem | primeira tela de tasks |
| `errors.rejectedAtEntryCount` e `errors.rejectionsByReason` | **acrescentado em 26/09**: nenhum artboard tem elemento para eles, e a D6 os tinha posto num grupo que esta regra proíbe criar | primeiro pedido do dono de ver o motivo da recusa |
| `errors.byProviderAndModel` parcial | entra, pela D9 | — |

**Acrescentar elemento que o artboard não tem é divergência tanto quanto
removê-lo**, e a `system-insights-ui` já fixou a régua: criar um quadro novo
acrescenta à tela um elemento cuja única função é falar do que ela não mostra, e
o peso dele compete com os números que ela mostra. **O que sobrevive ao archive é
a issue.**

**A armadilha de `maxDepthAtWhichAgentRan` fica escrita aqui**, porque é ela que
a próxima change redescobre: se algum dia for desenhada, o rótulo tem de dizer
**"a maior profundidade em que este agente executou"** — uma posição na cadeia.
Chamar de "profundidade de delegação", como no escopo do sistema, faz a métrica
do agente afirmar o tamanho da cadeia do sistema. O nome do campo já carrega a
diferença (`MaxDepthAtWhichAgentRan`, não `MaxObservedDelegationDepth`), e o
requisito na spec cobre o rótulo **caso** ela seja apresentada.

### D11 — A posição do `caveat` passa a ser por superfície; o texto continua único

**Decisão:** `caveatLabels.ts` mantém **um** mapa fechado de código → texto, e
ganha **um mapa de posição por superfície**. `caveatsFor(codes, placement,
surface)` passa a receber de qual superfície se fala.

**Por quê:** o mesmo código limita elementos diferentes nas duas telas, e a D4 é
o caso concreto: `residual-is-not-only-tools` é `not-on-this-page` na página do
sistema e **tem posição** na aba do agente. O inverso também existe —
`point-in-time-only` tem posição lá e não tem aqui (D10). Um mapa único de
posição obrigaria uma das duas a mentir.

**O texto continua único de propósito:** o que o código diz sobre a medição não
muda de tela para tela, e duplicá-lo criaria dois lugares onde a mesma frase pode
divergir (convenção 2).

**Os códigos desta superfície, e onde cada um fica:**

| código | bloco da resposta | posição na aba |
|---|---|---|
| `embedding-covers-search-only` | `tokens` | `not-on-this-page` (D10) |
| `submitted-at-missing-on-redelivery` | `performance` | junto da duração da task |
| `residual-is-not-only-tools` | `performance` | junto da parcela "Fora do provedor" |
| `rejections-missing-from-executions` | `errors` | junto da contagem de recusas |
| `point-in-time-only` | `errors` | `not-on-this-page` (D10) |
| `delegation-sides-are-not-mirrors` | `delegation` | dentro do card de delegação |

**Conferido contra os handlers:** são **seis** códigos em **quatro** blocos
(`tokens`, `performance`, `errors`, `delegation`). Dois blocos a mais que na
página do sistema, que recebe cinco em dois. `rejection-reason-not-collected`
**não chega em nenhum dos dois** — caiu com a #51.

### D12 — A aba é dona do período e da consulta; os cards são de apresentação

**Decisão:** `AgentInsightsTab` guarda o período escolhido e faz a consulta.
Nenhum card importa hook de query; todos recebem props.

**Por que não na página:** `AgentDetailPage` já hospeda quatro abas e não tem por
que carregar estado de período. **E o idioma da casa autoriza:**
`AgentDelegationsTab` e `AgentKnowledgeTab` já guardam as suas próprias mutações.
O que a convenção 7 protege é o **componente de apresentação** ser testável sem
`QueryClient`, e isso continua valendo para os sete cards.

**O carregamento tardio sai de graça:** o `<Tabs keepMounted={false}>` da página
já garante que só a aba ativa existe no DOM, então a consulta não dispara enquanto
o operador estiver em outra aba — sem precisar do `enabled:` que as abas de
ferramentas e conhecimento usam. O guarda afirma isso, porque é comportamento
fácil de perder numa mudança de `keepMounted`.

**A chave é `['insights', 'agent', id, período]` e a janela nasce dentro do
`queryFn`.** Se a janela entrasse na chave, cada render geraria chave nova — o
relógio andou — e a aba ficaria presa em carregamento. É o mesmo modo de falha
que `useSessions.ts` documenta e que `useSystemInsights.ts` já evita.

### D13 — O nome e o cadastro do agente vêm do catálogo que a página já busca

**Decisão:** `AgentInsightsTab` recebe por prop o agente (`agent`) e o catálogo
(`agentsCatalog`), que `AgentDetailPage` **já consulta hoje** — `useAgentsQuery`
é chamada sem condição, para a aba de delegações. **Nenhuma requisição nova.**

O cruzamento vive em `utils/delegationRows.ts`, função pura, e resolve as duas
distinções da D14 de uma vez:

- **nome** — `targetAgentId`/`sourceAgentId` → nome do catálogo; sem catálogo, o
  identificador, e a coluna de nome fica em carregamento ou em falha própria;
- **cadastro de saída** — `agent.delegatesTo`, que o detalhe já traz;
- **cadastro de entrada** — os agentes do catálogo cujo `delegatesTo` contém o
  agente consultado. **É derivável sem rota nova**, conferido em
  `ListAgentsQueryHandler`: a listagem devolve `AgentResponse` completo, com
  `DelegatesTo` por agente.

### D14 — Cadastro e uso são fatos diferentes, e o cruzamento é onde isso vive

**Decisão, nas duas seções:**

| situação | apresentação |
|---|---|
| cadastrado **e** com ocorrência | linha com a contagem medida |
| cadastrado **e** sem ocorrência | linha com **`0`** — contagem feita sobre vínculo que existe |
| **sem** cadastro nenhum | o quadro tracejado, **sem número** |
| ocorrência medida **sem** cadastro atual | linha com a contagem, identificada pelo catálogo |

**A frase é do próprio artboard:** *"'sem delegação cadastrada' não é o mesmo que
um vínculo que existe e não foi usado no período, que apareceria como linha com
contagem zero."* É a gramática dos quatro estados aplicada a linhas em vez de
células: o `0` é reservado à contagem feita, e a ausência de cadastro não é
contagem nenhuma.

**A quarta linha é caso real** e o artboard não a cobre: vínculo removido depois
de ter sido usado. Omitir a linha apagaria medição que aconteceu.

**E o texto do tracejado é corrigido.** O `Agente-Insights.dc.html` escreve
*"Nenhum agente aciona este. Ele recebe pedidos externos."* A segunda frase só é
verdadeira quando `externalOriginTaskCount > 0` — no **quarto cenário**, agente
sem nada dos dois lados, ela afirmaria uma origem que não houve. A segunda frase
passa a ser condicional. Divergência registrada.

### D15 — `KpiCard` é extraído agora porque a repetição foi **observada**

**Decisão:** `KpiCard` e o auxiliar `Part` saem de dentro de `InsightsKpiGrid.tsx`
para `components/KpiCard.tsx`, e os dois grids passam a importá-los.

**Por quê agora e não na #52:** a convenção 2 exige repetição **já observada**,
nunca prevista, e a #52 registrou por escrito que a etapa 5 era consumidor
**previsto**. Ela chegou: a #52 é o primeiro consumidor, esta é o segundo. Os
três componentes visuais compartilhados do painel saíram de cinco cópias, quatro
estruturas iguais e três cabeçalhos repetidos — **contados antes de extrair**, e
é a mesma régua aqui.

**O que NÃO é extraído, e por quê:** `AgentKpiGrid` é componente próprio, não
`InsightsKpiGrid` parametrizado — são quatro cards contra seis, com rótulos,
subtítulos e fontes diferentes, e o tipo da resposta é outro. Parametrizar o grid
inteiro juntaria duas telas num componente que muda por bandeira.

### D16 — `agentInsights.ts` reusa as formas neutras de escopo, como o C# faz

**Decisão:** o contrato do escopo do agente **importa** de `systemInsights.ts` as
interfaces que significam a mesma coisa nas duas rotas — `InsightsWindow`,
`DailyInsightPoint`, `WeekdayInsightPoint`, `TemporalInsights`, `DurationStats`,
`TokenTotals`, `ModelTokens`, `TokensPerTask`, `FailurePhaseCount`,
`NonTerminalTasks` — e declara **só** o que é próprio.

**Espelha a decisão que o C# já tomou** e escreveu: aqueles records são formas
neutras de escopo, e duplicá-las criaria dois lugares onde o mesmo conceito pode
divergir. É também o que permite `measuredDays` e `WeekdayActivityCard` serem
reusados sem adaptador.

**Todo campo anulável entra como `| null`, e nenhum recebe default.** É a primeira
das três linhas de defesa da gramática — um `?? 0` no tipo apagaria a distinção
antes de qualquer componente ver o dado.

### D17 — O `404` é estado próprio, sem nova tentativa

**Decisão:** `ApiError` com `status === 404` vira um estado próprio da aba: um
texto que nomeia a recusa, **sem número nenhum na tela** e **sem "Tentar de
novo"**.

**Por quê:** `404` é uma resposta, não uma falha de comunicação. Oferecer nova
tentativa ao lado dele convida a repetir uma pergunta cuja resposta não vai
mudar — e é a confusão que o `DeclaredGap` já registra ter contido por não ter
ação de retentativa ao lado.

**E na prática ele só aparece se as duas rotas discordarem.** A página de detalhe
já resolveu o agente antes de a aba existir, e não há `DELETE /agents/{id}` no
repositório — conferido. O caso alcançável é a discordância entre a rota de
catálogo e a de métricas, e o texto diz isso em vez de dizer "não encontrado",
que o operador leria como erro dele.

**O guarda é o par:** `404` não renderiza `0` em lugar nenhum, e `200` com tudo
zerado renderiza os `0` como contagem feita. Um sem o outro não discrimina nada.

## Riscos / Trade-offs

- **[A conferência manual volta a custar dez rodadas]** → A #52 gastou treze, e
  nenhum achado foi de lógica. Aqui a leitura dos artboards foi tarefa 0, o mapa
  elemento → campo está escrito acima, e **sete divergências já estão nomeadas
  antes do primeiro componente** — é o que sobra para o dono achar que fica
  menor. Mitigação parcial, não eliminação: o desenho tem decisões que só
  aparecem montadas.
- **[A linha de detalhe de resultado da D2 engorda o card e desmancha o
  ranking]** → A #52 mediu isto: *"a lacuna engordou o card"* foi rodada própria
  de conferência, e lacuna declarada teve **consequência mensurável em outro
  elemento da tela**. Está na lista da conferência manual, com a alternativa
  recusada escrita para a troca não parecer capricho.
- **[O `0` de vínculo cadastrado e ocioso ser lido como erro]** → Ele é contagem
  feita, e a gramática o autoriza. O risco real é o inverso — alguém "consertar"
  para o tracejado —, e o guarda é negativo: afirma que vínculo cadastrado sem
  ocorrência **não** produz o estado de ausência de vínculo.
- **[A ausência da barra empilhada (D5) parecer componente não implementado]** →
  O card mantém os três números com os seus rótulos e `sampleCount`; não há
  espaço vazio. O que sai é a área, e a razão vai para a issue, não para a tela.
- **[Mexer em `caveatLabels.ts` quebrar a página do sistema]** → O arquivo é
  consumido pelos dois. A assinatura ganha a superfície com **valor padrão na
  superfície do sistema**, para que nenhum dos cinco sítios existentes mude de
  comportamento, e a suíte da página do sistema roda antes e depois.
- **[Ciclo de import entre as features `agents` e `insights`]** → Não há: a aba
  recebe agente e catálogo **por prop** e não importa nada de `features/agents`.
  A seta é de mão única, `agents` → `insights`, e o guarda é a própria ausência
  de import — afirmada por teste, porque é o tipo de acoplamento que volta numa
  refatoração distraída.
- **[A projeção da convenção 18 errar para baixo nas linhas de teste]** → Errou
  +140% na vigésima e +84% nos casos. Aqui a semeadura de fixture entra como
  **item próprio** da tabela, que foi o refinamento que aquela medição produziu.

## Convenção 18 — vigésima primeira medição, projeção

**Unidade declarada antes de medir:** cenários de delta separados em novos e
copiados; arquivos criados × modificados **separados**, contados da árvore acima
linha a linha; casos de teste em **quatro** categorias — novo, reforçado,
adaptado e **arranjo compartilhado por fixture**, que a vigésima descobriu não
ser caso; linhas em **três** níveis (produção à mão, teste à mão, duplos), com
gerado em **0**.

**A âncora é a #52, e é uma só.** É a única tela de dados da série: 24 arquivos
de produção / **3.141 linhas** (média 131), 13 de teste / **3.359 linhas** sem o
duplo, **291 casos** (11,5 linhas por caso) e um duplo de **140 linhas**. Esta
aba tem sete cards contra doze elementos, reusa oito módulos prontos e não tem
mapa de calor nem série diária.

| dimensão | projetado |
|---|---|
| cenários de delta | **53** — 46 novos, 7 copiados (os do `MODIFIED` de `agent-catalog-ui`) |
| arquivos criados | **26** — 13 produção + 13 teste (um deles o duplo) |
| arquivos modificados | **10** — 5 produção + 5 teste |
| casos de teste | **~155 novos**, ~10 adaptados, 0 reforçados |
| linhas de produção à mão | **~1.700** |
| linhas de teste à mão | **~1.900** |
| duplos | **~170** |
| **linhas geradas** | **0** |

**A proporção comentário:lógica é por tipo de arquivo, não por change** — foi a
régua que a vigésima mediu (2,5:1 e 2,78:1 em registro de decisão contra 1,11:1 e
1,14:1 em mecanismo). Os 13 criados de produção separados por tipo:

| tipo | arquivos | proporção projetada | linhas |
|---|---|---|---|
| registro de decisão | 4 — `agentInsights.ts`, `delegationRows.ts`, `AgentDelegationCard.tsx`, `TimeBreakdownCard.tsx` | **2,5:1** | ~700 |
| mecanismo | 9 — os demais | **1,15:1** | ~1.000 |

Os quatro de registro são os que carregam a assimetria (D1, D2, D14), a
transcrição do contrato com os renomes (D16) e a recusa da barra (D5). Os nove
de mecanismo montam card.

**A direção de erro que estou nomeando, sabendo que nomear não substitui
contar:** as linhas de teste, por causa da semeadura — e a mitigação é que
`agentInsightsFixture.ts` é **um** duplo, com sobrescrita por bloco, no molde de
`systemInsightsFixture.ts`, para que os cenários que diferem num campo custem
linhas de caso e não de arranjo.

**Fechamento só compara.** A projeção não é revisada depois; `#### Scenario:` é
contado nos arquivos de delta, com os do `MODIFIED` separados, e `git diff -w`
mede o modificado.

## Migration Plan

Não há migração de dados, de schema nem de contrato. A aba é aditiva: nenhuma
superfície existente muda de comportamento, e a única mudança fora de
`features/insights/` é a quinta aba no detalhe do agente.

**Reversão:** remover a aba de `AgentDetailPage.tsx` devolve a página ao estado
atual; o restante da feature fica órfão e inerte.

**Ordem de implantação:** irrelevante — `apps/api` já serve a rota em produção, e
não há janela em que o painel novo peça algo que o servidor não tenha.

## Open Questions

Apenas incertezas reais de produto, que a conferência manual do dono resolve
(convenção 14). Nenhuma delas bloqueia a implementação: cada uma tem um padrão
decidido acima, e a pergunta é se o dono o mantém.

1. **As quatro métricas servidas e não desenhadas (D10) devem ficar fora?**
   Padrão aplicado: ficam fora, porque o artboard não as tem. A que mais pesa é
   `maxDepthAtWhichAgentRan`, que é própria deste escopo e não existe na página
   do sistema com este significado.
2. **A linha de resultado discriminado (D2) cabe no card sem desmanchar o
   ranking?** Padrão aplicado: cabe, como linha de detalhe sob a barra. É a que
   mais provavelmente muda na conferência.
3. **A #67 fecha sem código?** **Esta change não decide** — só produz a
   evidência. Ao entregar a aba, ficará medido se tasks, tokens por task e
   duração p95 **por agente** ficam legíveis aqui a ponto de as três colunas não
   precisarem entrar no ranking do sistema. O que a implementação mostrar vai
   para o `02` e para a **#67**, com o gatilho cumprido e a decisão do dono.
