**Todas as tarefas desta change rodam em `apps/frontend`.** Nenhuma toca
`apps/api`, `apps/workers`, `apps/inbox`, `libs/` ou `stack/`. Onde uma tarefa
citar outro app, ela é de **leitura** ou de **registro**, e está marcada assim.

## 0. O protótipo é a tarefa 0, e o contrato vem do código

- [x] 0.1 **Abrir os artboards antes de qualquer decisão de componente.** Feito
      em 26/09/2026, pela ferramenta `Artifact` — o MCP `claude-design` recusa a
      conexão (`FIRST_PARTY_AUTH_REJECTED`). Canvas
      `https://claude.ai/artifact/R9JSCiYw7pBEognkKhiaDh`, versão
      `1790386152-b562`. Lidos os seis arquivos de `project/`:
      `Agente-Insights.dc.html`, `Agente-Delegado.dc.html`,
      `Agente-Misto.dc.html`, `Estados.dc.html`, `Insights-Claro.dc.html` e
      `canvas.json`. **Se a leitura tivesse falhado, a change pararia aqui** —
      não se trabalha do `02` nem de `design.md` arquivado, e o precedente é a
      `rotas-de-agregacao-sistema`, que teve de declarar no `02` que não abriu o
      protótipo.
- [x] 0.2 **Ler o `canvas.json`**, que traz o que registro nenhum tem.
      **Confirmado:** a nota `cenarios` fixa o **quarto cenário** — agente que
      não delega nem é delegado é *"o mesmo card com as duas seções
      tracejadas"* —, e a nota `estados` dá a autoridade da regra de divergência
      com as palavras do autor. As duas estão transcritas no `design.md`.
- [x] 0.3 **Conferir o deslize do quadro 4.** Título diz "três estados", desenho
      tem **quatro** linhas — medido, célula vazia, travessão, zero. O desenho
      manda, e `utils/metricState.ts` já implementa os quatro.
- [x] 0.4 **Conferir que os artboards do agente NÃO têm mapa de calor nem série
      diária.** Confirmado nos três: os quadros são KPIs, "onde o tempo foi" +
      duração, modelos, dias da semana + delegação, falhas. O
      `Insights-Claro.dc.html` é a página do **sistema** no tema claro, não esta
      aba — logo a inversão da escala de calor, já contratada em
      `frontend-visual-theme`, não é exercitada aqui, e `PeriodHeatmapCard` e
      `DailyTasksCard` ficam fora.
- [x] 0.5 **Ler o contrato da rota no código, não de memória** (convenção 6) —
      `apps/api/src/Buteco.Api/Insights/Responses/AgentInsightsResponse.cs` e os
      dois handlers. **Leitura**, não alteração. Registrado no `design.md` o
      mapa elemento → campo, e confirmado que o tipo **não tem** `ByAgent` nem
      `IndexingFailures`.
- [x] 0.6 **Contar os códigos de parcialidade por bloco**, contra
      `GetAgentInsightsQueryHandler`: **seis** códigos em **quatro** blocos —
      `tokens` (1), `performance` (2), `errors` (2) e `delegation` (1). Dois
      blocos a mais que na página do sistema, e `rejection-reason-not-collected`
      **não chega em nenhum dos dois escopos** desde a #51.
- [x] 0.7 **Conferir o corpo real da rota** contra o mapa do `design.md`, com
      `apps/api` local e **`TZ=America/Sao_Paulo`** no ambiente — sem ele a
      checagem de boot reprova com `'Brazil/East'`. **Feito em 26/09/2026**, com
      uma segunda instância no `HEAD` (`5792ec8`) na porta **5018**, para não
      mexer no processo que o dono já tinha na 5017. Janela de 90 dias, dois
      agentes (`Triagem`, que tem vínculo cadastrado, e `Gestor de Reservas`).
      **Confere ponto a ponto:** três regimes (`execution`, `embedding`,
      `rejection`); `errors` com as dez chaves esperadas, inclusive `regime`
      (**e não** `executionRegime`, que é o nome no gêmeo do sistema),
      `rejectionRegime`, `rejectedAtEntryCount` e `rejectionsByReason`;
      `maxDepthAtWhichAgentRan` presente; **seis** `caveats` em **quatro**
      blocos, exatamente os da tabela da D11, e `rejection-reason-not-collected`
      **ausente**. Os quatro códigos de resposta conferidos: `404` para id
      inexistente, `400` para janela inválida **mesmo com id inexistente** (a
      ordem de validação é contrato), `200` para agente **inativo** e `401` sem
      token.
      **Dois achados da conferência:**
      **(a)** a instância que o dono tinha de pé na 5017 é **anterior à #51** —
      devolve `rejection-reason-not-collected` e não tem os três campos novos.
      Não é defeito do código, é processo velho; anotado para não confundir quem
      conferir depois;
      **(b)** o dado de dev tem `delegatesTo` e `triggeredBy` **vazios** com
      vínculo **cadastrado** entre `Triagem` e `Gestor de Reservas` — que é
      exatamente o caso "cadastrado e ocioso" da D14, e vale de semente na
      conferência manual.
- [x] 0.8 **Medir as baselines na abertura, declarando o `podman ps`.** Elas
      **não são herdadas** (convenção 22). **Medidas em 26/09/2026 sobre
      `5792ec8`, árvore limpa, antes de tocar em qualquer arquivo**, com
      `buteco-agents_postgres_1` (pgvector/pgvector:pg18) e
      `buteco-agents_rabbitmq_1` (rabbitmq:4.3-management) de pé e *healthy*, e
      `waha` parado.

      | suíte | 26/09 anterior | **baseline desta change** |
      |---|---|---|
      | `apps/frontend` | 1218 / 107 arquivos | **1218 / 107**, 2m09s |
      | `apps/api` | 439 | **439**, 2m29s |
      | `apps/workers` | 386 | **386**, 8m51s |
      | `apps/inbox` | 203 **com 3 flakes** (#61) | **203/203**, 24s — sem flake nesta rodada |

      **O regime das três suítes de backend é `DOCKER_HOST` + Ryuk desligado, e
      isso teve de ser descoberto.** O item de memória desta máquina dizia só
      "sem `DOCKER_HOST` a suíte falha inteira", e o caminho registrado era
      `unix:///run/user/501/podman/podman.sock` — que é o socket **de dentro da
      VM**. Na `podman machine` de macOS o socket do host é outro, e sai de
      `podman machine inspect --format '{{.ConnectionInfo.PodmanSocket.Path}}'`.
      Com ele e **sem** `TESTCONTAINERS_RYUK_DISABLED=true`, as três suítes
      ainda reprovavam em bloco na inicialização de fixture — 369/439, 250/386,
      131/203, todas em menos de 10 s, que é o sintoma de contenção que a régua
      de contenção de `apps/api` nomeia. Com as duas variáveis, verde.
      **É achado ambiental a registrar**, e a conclusão de "pré-existente" só
      pôde ser descartada porque a baseline foi rodada (convenção 19).

      **`apps/inbox` deu 203/203**, contra os três flakes nomeados na #61.
      Uma rodada verde **não fecha** a #61: flake que não reproduz numa rodada
      continua flake, e o valor deste número é ser a baseline de comparação,
      não um veredito sobre a issue.

## 1. O contrato do escopo do agente e a consulta

- [x] 1.1 Criar `src/features/insights/types/agentInsights.ts` transcrevendo
      `AgentInsightsResponse.cs` em `camelCase`. **Importar de
      `systemInsights.ts`** as formas neutras de escopo — `InsightsWindow`,
      `TemporalInsights`, `DailyInsightPoint`, `WeekdayInsightPoint`,
      `DurationStats`, `TokenTotals`, `ModelTokens`, `TokensPerTask`,
      `FailurePhaseCount`, `NonTerminalTasks` — e declarar só o que é próprio
      (D16). Todo anulável entra como `| null`; **nenhum** campo recebe default,
      `?? 0` ou `Number(x)`.
- [x] 1.2 Comentar no arquivo, ao lado dos campos, as **três** diferenças que o
      tipo carrega e que um leitor distraído reintroduz: não há `byAgent`, não
      há `indexingFailures`, e `searchEmbeddingInputTokens` **não** é
      `embeddingInputTokens`.
- [x] 1.3 Acrescentar `getAgentInsights(id, from, to)` a
      `src/features/insights/api/insightsApi.ts`, reusando o `periodQuery` que
      já está lá. Os dois limites continuam obrigatórios.
- [x] 1.4 Acrescentar a `insightsApi.test.ts`: a URL montada com o identificador
      e os dois limites; o **`404` chegando como `ApiError` com `status` 404**,
      distinguível do `400` de janela inválida; o `401` limpando o token.
- [x] 1.5 Criar `src/features/insights/api/useAgentInsights.ts` — `useQuery` com
      chave `['insights', 'agent', id, período]` e a janela calculada **dentro
      do `queryFn`** (D12). Comentar por que a janela não entra na chave,
      apontando para `useSystemInsights.ts` e `useSessions.ts`.
- [x] 1.6 Escrever `useAgentInsights.test.tsx`: uma consulta por período; dois
      agentes diferentes não compartilham cache; renders repetidos sem troca de
      período não geram consulta nova; nova tentativa recalcula a janela.
- [x] 1.7 Criar `src/features/insights/test/agentInsightsFixture.ts` — o **duplo
      único** desta change, com sobrescrita **por bloco**, no molde de
      `systemInsightsFixture.ts`. É ele que torna barato escrever os cenários que
      diferem num campo, e é **arranjo compartilhado**, não caso (a categoria que
      a vigésima medição descobriu).
- [x] 1.8 Dar à fixture construtores para os **quatro** cenários de delegação —
      só delega, só é delegado, os dois, nenhum dos dois — e para o cenário em
      que os dois lados **divergem**. Sem eles, cada teste remonta o arranjo.

## 2. Os utilitários puros

- [x] 2.1 Criar `utils/delegationOutcomeLabels.ts` com os **quatro** resultados
      de `ExecutionMetricsValues.DelegationOutcome` — `Completed`,
      `TargetUnsuccessful`, `Expired`, `NotStarted` — em rótulo de operador.
      Valor desconhecido devolve apresentação **neutra com o próprio valor**, no
      idioma de `failurePhaseLabels.ts`.
- [x] 2.2 Escrever `delegationOutcomeLabels.test.ts`: cada resultado conhecido; e
      o desconhecido devolvendo o valor cru, **não** o rótulo de outro resultado.
      Exportar a lista fechada para o teste afirmar que são quatro.
- [x] 2.3 Criar `utils/rejectionReasonLabels.ts` com os **quatro** motivos de
      `RejectionMetricsValues` — `AgentNotFound`, `AgentInactive`,
      `ProviderOrModelMissing`, `ProviderNotConfigured`. Mesmo tratamento do
      desconhecido.
- [x] 2.4 Escrever `rejectionReasonLabels.test.ts`, no mesmo molde da 2.2.
- [x] 2.5 Criar `utils/delegationRows.ts` — a função pura que cruza o agregado
      com o catálogo e devolve as linhas das **duas** seções (D13, D14). Entrada:
      o bloco `delegation`, o agente consultado e o catálogo. Saída: para cada
      lado, as linhas com identificação, contagem e a marca de **cadastrado** ×
      **medido**, mais a informação de que o lado **não tem cadastro nenhum**
      (que é o tracejado) — distinta de "tem cadastro e a contagem é zero".
- [x] 2.6 Derivar o cadastro **de entrada** varrendo o `delegatesTo` dos agentes
      do catálogo. **Conferido em `ListAgentsQueryHandler`** (leitura de
      `apps/api`): a listagem devolve `AgentResponse` completo, com `DelegatesTo`
      por agente — não há rota nova a pedir.
- [x] 2.7 Escrever `delegationRows.test.ts` com os guardas, e três deles são
      **negativos**: vínculo cadastrado e ocioso vira linha com `0` e **não** o
      estado de ausência; ausência de cadastro **não** produz nenhum `0`;
      ocorrência medida sem cadastro atual **não** é omitida; catálogo ausente
      não apaga linha nenhuma, só o nome.
- [x] 2.8 Modificar `utils/caveatLabels.ts` (D11): acrescentar os textos de
      `embedding-covers-search-only` e `delegation-sides-are-not-mirrors`, e
      separar **texto** (mapa único) de **posição** (mapa por superfície). A
      assinatura de `caveatsFor` ganha a superfície **com padrão na do sistema**,
      para que os cinco sítios existentes não mudem de comportamento.
- [x] 2.9 Preencher a posição da superfície do agente exatamente como a tabela da
      D11: `residual-is-not-only-tools` **tem** posição aqui (e não tem lá),
      `point-in-time-only` e `embedding-covers-search-only` são
      `not-on-this-page` aqui. Comentar ao lado que a assimetria entre as duas
      tabelas **é a razão de a posição ser por superfície**.
- [x] 2.10 Acrescentar a `caveatLabels.test.ts`: os seis códigos da aba com a
      posição de cada um; o mesmo código com posição **diferente** nas duas
      superfícies; o desconhecido aparecendo em vez de sumir; e a suíte da página
      do sistema continuando verde sem alteração.
- [x] 2.11 **(descartável pelo dono)** Remover a entrada morta
      `rejection-reason-not-collected` do mapa fechado. O código saiu dos dois
      handlers de `apps/api` com a #51 e os testes de lá afirmam a ausência dele.
      Tarefa própria de propósito: se o dono preferir tratá-la junto da issue da
      página do sistema, ela cai sem afetar nada mais.

## 3. Os componentes de apresentação

Nenhum destes importa hook de query. Todos recebem props, e todos são testáveis
sem `QueryClient` (convenção 7).

- [x] 3.1 Extrair `KpiCard` e o auxiliar `Part` de `InsightsKpiGrid.tsx` para
      `components/KpiCard.tsx`, sem mudar comportamento, e fazer
      `InsightsKpiGrid` importá-los (D15). Comentar que a extração é da
      **repetição observada**, com os dois consumidores nomeados.
- [x] 3.2 Rodar a suíte da página do sistema depois da extração e afirmar que ela
      passa **sem alteração nos testes** — é o que prova que a extração foi
      mecânica.
- [x] 3.3 Criar `components/AgentKpiGrid.tsx` — os **quatro** cards do artboard,
      na ordem dele, com os tamanhos de `METRIC_SIZE.kpi`. Subtítulos pela
      gramática: zero medido por extenso ("nenhuma"), célula vazia **vazia**,
      travessão só com a consulta sem resposta.
- [x] 3.4 No KPI de falha, aplicar a D6: o subtítulo traz `failedCount` e
      `rejectedCount`, e **não** `rejectedAtEntryCount`, que é de outro regime.
      Quando `failedCount` é `0`, o valor do card sai por extenso ("Nenhuma") e o
      subtítulo vira "em N tasks executadas", como o `Agente-Delegado.dc.html`
      desenha.
- [x] 3.5 Escrever `AgentKpiGrid.test.tsx` com as asserções **negativas**: fonte
      nula **não** produz `0` em nenhum dos quatro; a consulta em curso **não**
      produz `0`; o zero medido **não** vira célula vazia; e o subtítulo de falha
      **não** apresenta a soma das duas recusas.
- [x] 3.6 Criar `components/TimeBreakdownCard.tsx` — "Onde o tempo foi",
      **sem barra proporcional** (D5), com as três parcelas nomeadas, cada uma
      com o seu número, o seu estado e o seu `sampleCount`. A terceira é **"Fora
      do provedor"**, não "Ferramentas" (D4), com o texto de
      `residual-is-not-only-tools` ao lado. O cabeçalho **não** afirma um total.
- [x] 3.7 Comentar no arquivo, com a tabela do `design.md`, **por que** a barra
      não é desenhada: as três parcelas medem populações diferentes, o tempo de
      provedor **por task** não é servido, e os dois caminhos de derivação
      produzem um número plausível que não é média de coisa nenhuma. É um dos
      quatro arquivos de registro de decisão da projeção.
- [x] 3.8 Escrever `TimeBreakdownCard.test.tsx`: as três parcelas com valor;
      parcela nula **não** vira `0` nem largura zero; e o guarda de **ausência de
      área** — nenhum elemento de proporção é renderizado.
- [x] 3.9 Criar `components/TaskDurationCard.tsx` — os três quadros internos do
      artboard, com `METRIC_SIZE.card`. O primeiro é **"Média"**, não "Mediana"
      (D3), e o texto de `submitted-at-missing-on-redelivery` vai junto dele.
- [x] 3.10 Escrever `TaskDurationCard.test.tsx`, incluindo o guarda de rótulo: a
      palavra "mediana" **não** aparece no card.
- [x] 3.11 Criar `components/AgentModelsCard.tsx` — a tabela Modelo / Provedor /
      Chamadas / Tokens, ordenada como o artboard, **sem a coluna "Cache lido"**
      (D7). A nota de rodapé que explica a cardinalidade é a do artboard, e diz
      **mudança de configuração daquele agente**, nunca comparação entre agentes.
- [x] 3.12 Acrescentar ao rodapé a **lacuna declarada inline** da separação turno
      × compactação, com o qualificador **"não devolvida por esta rota"** — e
      não "não coletada", que é o erro com o sinal trocado que a #52 corrigiu:
      `ProviderCall.Purpose` **é gravado**.
- [x] 3.13 Escrever `AgentModelsCard.test.tsx`: a tabela com duas linhas e a nota
      de mudança de configuração; **uma linha só não afirma que a configuração
      nunca mudou**; `totalTokens` nulo produz **célula vazia e não `0`** (a
      asserção negativa que herdou o papel da coluna de cache); `callCount` em
      `0` produz zero medido; a lacuna declarada existe e o texto dela **não**
      diz "não coletada", "falhou" nem "tentar de novo"; e **nenhuma** coluna de
      cache é renderizada.
- [x] 3.14 Modificar `components/WeekdayActivityCard.tsx` acrescentando a prop
      opcional `note` (D8), sem mudar nada do comportamento atual, e acrescentar
      o caso a `WeekdayActivityCard.test.tsx`: sem `note` a página do sistema
      renderiza como hoje.
- [x] 3.15 Criar `components/AgentDelegationCard.tsx` — as **duas** seções,
      sempre presentes, cada uma com linhas ou com o quadro tracejado (D1, D14).
      O texto de `delegation-sides-are-not-mirrors` fica **dentro** do card.
- [x] 3.16 Na seção "Delega para", renderizar a linha por destino com a contagem
      total e, sob ela, os **resultados discriminados** (D2). Resultado
      desconhecido aparece cru.
- [x] 3.17 Tornar condicional a segunda frase do tracejado de "Acionado por": ela
      só afirma que o agente recebe pedidos externos quando
      `externalOriginTaskCount > 0` (D14, quarto cenário).
- [x] 3.18 Comentar no arquivo a razão da assimetria com as **duas** causas, e
      que **bater é possível e não é garantido** — hoje os números batem em
      produção porque a janela não tem `NotStarted` nem nada na fronteira. É o
      segundo arquivo de registro de decisão da projeção.
- [x] 3.19 Escrever `AgentDelegationCard.test.tsx`, e aqui ficam os guardas mais
      importantes desta change:
      **(a)** cenário em que os dois lados **divergem** para a mesma relação, com
      a asserção **afirmando a divergência** — os dois números aparecem, nenhum é
      reconciliado e nenhum aviso de inconsistência é renderizado;
      **(b)** os **quatro** cenários, cada um com as duas seções presentes;
      **(c)** vínculo cadastrado e ocioso vira `0` e **não** tracejado;
      **(d)** ausência de cadastro **não** produz `0`;
      **(e)** os resultados discriminados aparecem, e a tela **não** apresenta só
      a soma deles;
      **(f)** quando os dois lados **coincidem**, a tela continua com dois
      conjuntos e **não** afirma espelho;
      **(g)** o texto do tracejado de "Acionado por" **não** afirma origem
      externa no quarto cenário;
      **(h)** nenhum total soma os dois lados.
- [x] 3.20 Criar `components/AgentFailuresCard.tsx` — os grupos rotulados por
      fase e por provedor e modelo (D9), mais o grupo das **recusas de entrada**
      com o **regime de recusa** declarado no cabeçalho dele (D6). Sem falha no
      período, o quadro tracejado com o total que chegou a concluído, e **não**
      tabela vazia.
- [x] 3.21 Escrever `AgentFailuresCard.test.tsx`: as três populações separadas;
      **nenhum** número apresentado é a soma de duas delas; motivo desconhecido
      aparece cru; o `caveat` de recusa aparece junto da contagem de recusas; o
      período sem falha usa o quadro tracejado com o total; e o card **não**
      renderiza coluna de servidor MCP, que nenhum campo serve.

## 4. A aba, a página e a composição

- [x] 4.1 Criar `components/AgentInsightsTab.tsx` — guarda o período, faz a
      consulta e compõe os sete cards na ordem do artboard (D12). Recebe `agent`
      e `agentsCatalog` por prop; **não importa nada de `features/agents`**.
- [x] 4.2 Montar o cabeçalho da aba com a janela **ecoada pela resposta**, o
      fuso dela e o "medindo desde" do regime que governa a aba, mais o
      `PeriodPicker` reusado. Cair no cálculo local **só** enquanto a resposta
      não chegou.
- [x] 4.3 Reusar `PartialMeasurementNotice` acima dos números, alimentado por
      `measuredDays(janela, temporal.dailySeries)` — o quadro 1 do
      `Estados.dc.html`, que abre a tela quando a janela pedida começa antes da
      medição.
- [x] 4.4 Modelar o estado da consulta em **três** valores — `ok`, `loading`,
      `failed` — com `ok` saindo **só** de `isSuccess`, nunca de `!isError`. É o
      defeito que a #52 levou doze rodadas para achar: com `!isError`, o pendente
      passava e a tela afirmava `0` com a API fora do ar.
- [x] 4.5 Acrescentar o **quarto** estado, próprio desta aba: `404` (D17). Texto
      que nomeia a recusa, **nenhum número na tela**, **sem "Tentar de novo"**, e
      distinto do texto de consulta sem resposta.
- [x] 4.6 Renderizar os `caveats` desconhecidos como aviso visível, no idioma que
      a página do sistema já usa.
- [x] 4.7 Modificar `src/features/agents/pages/AgentDetailPage.tsx`: quinta aba
      `insights`, depois de delegações, **sem contador**; `parseTab` passa a
      aceitar o valor novo; a aba recebe `agent` e `agentsCatalog`.
- [x] 4.8 Escrever `AgentInsightsTab.test.tsx`: uma requisição por abertura;
      pendente e falha produzindo travessão com razão e **nenhum** `0`; `404`
      distinguível de `200` zerado, com o par afirmado nos **dois** sentidos;
      `200` zerado com os `0` como contagem feita; agente **inativo** com a aba
      cheia; janela vinda da resposta; e o guarda de acoplamento — o módulo
      **não** importa `features/agents`.
- [x] 4.9 Acrescentar a `AgentDetailPage.test.tsx`: cinco abas na ordem; a de
      insights **sem contador** em qualquer agente; `?tab=insights` reabrindo a
      aba; valor desconhecido caindo na visão geral; e o guarda de **consulta não
      disparada** enquanto a aba está inativa.

## 5. Verificação da suíte

- [x] 5.1 Rodar `npm run lint` e `npx tsc --noEmit` em `apps/frontend`.
- [x] 5.2 Rodar a suíte inteira de `apps/frontend` e comparar com a baseline da
      tarefa 0.8, **por nome** quando o número não bater.
- [x] 5.3 **Verificar por mutação os três guardas que mais valem** (convenção 15
      — um guarda só vale depois de ter falhado contra o defeito real):
      **(a)** fazer os dois lados da delegação se reconciliarem e ver o guarda da
      3.19(a) reprovar;
      **(b)** trocar o cruzamento por `?? 0` e ver o guarda da 2.7 reprovar;
      **(c)** trocar `isSuccess` por `!isError` na 4.4 e ver o guarda da 4.8
      reprovar.
      Registrar as três no `02`.
- [x] 5.4 **Não rodar** `apps/api`, `apps/workers` e `apps/inbox` se nenhuma
      linha delas entrar no diff, e **declarar isso** com a conferência de escopo
      da seção 8 ao lado — é o precedente da #52. Se alguma linha entrar, a
      suíte correspondente roda e o escopo é reexaminado.

## 6. Conferência manual — do dono (convenção 14)

A suíte roda em jsdom, que não enxerga cor, contraste, layout nem quebra de
linha. **O agente produz os estados; o dono julga.** O precedente que justifica o
custo é a `frontend-mensagem-recusa-ciclo`, que pegou um estado intermediário que
nenhum guarda pediria.

> **Estado em 26/09/2026: a aba foi exercitada contra o piloto e três capturas
> saíram; o resto depende de semeadura e do julgamento do dono.**
>
> Ambiente usado, para poder ser repetido: `apps/api` do `HEAD` numa **segunda**
> instância (`ASPNETCORE_URLS=http://localhost:5018`, `TZ=America/Sao_Paulo`) e
> `vite` na **4173**, que é porta já permitida pelo CORS de
> `appsettings.Development.json` — as duas escolhidas para não tocar nos
> processos que o dono tinha na 5017 e na 5173.
>
> Capturas em `capturas/` do diretório temporário do job:
> **01** aba inteira no escuro (`Triagem`), **02** a mesma no claro,
> **03** `Gestor de Reservas` no claro.
>
> **O que já se vê, e é resultado:**
> - a **03 exercita o quadro 5 do `Estados.dc.html` com dado real** —
>   `gemini-2.5-pro` com **Chamadas `1` e Tokens VAZIO na mesma linha**. É
>   exatamente a demonstração que a D7 prometeu ao mover a célula vazia da
>   coluna de cache para a de tokens, e ela apareceu sem semeadura nenhuma;
> - a **01 exercita o "cadastrado e ocioso"**: `Triagem` tem vínculo para
>   `Gestor de Reservas` e a janela não tem delegação — a linha sai com `0`, e
>   não com o tracejado (D14);
> - a **03 exercita "Sem delegação cadastrada"** no outro lado;
> - o tracejado de "Acionado por" saiu **sem** a frase "Ele recebe pedidos
>   externos" no agente sem origem externa — a condicional da D14 funciona no
>   piloto;
> - o quadro 1 (`período maior que a medição`) aparece nas três.
>
> **O que NÃO foi capturado, e por quê:**
> - **os quatro cenários de delegação** pedem semeadura de `delegation_outcomes`
>   e de execuções por delegação no banco de dev, que hoje não os tem. Semear o
>   banco do dono é decisão dele;
> - **o estado de consulta sem resposta da ABA** não é alcançável derrubando a
>   API inteira: a **página** falha antes, em `useAgentQuery`, e o que aparece é
>   "Não foi possível carregar o agente". Ele exige indisponibilidade **parcial**
>   — rota de catálogo de pé e rota de métricas fora —, o que pede intercepção
>   de requisição. Os guardas cobrem o estado; a captura, não;
> - **o `404` da aba** tem a mesma natureza, e a D17 já registra que ele só é
>   alcançável se as duas rotas discordarem.
>
> **Achado de operação, registrado aqui porque custou tempo:** `pkill -f
> "Buteco.Api"` derruba **todas** as instâncias, inclusive a que o dono tinha na
> 5017. Ela foi restaurada na mesma sessão. Para derrubar uma só, usar o PID.

> ### Como abrir, para a conferência do dono
>
> **O ambiente que o dono já tinha serve a aba**, conferido em 26/09: `vite` na
> **5173** entrega `AgentInsightsTab.tsx`, e o `.env` aponta para a **5017**, que
> foi reiniciada no `HEAD` e já devolve `rejectedAtEntryCount`. Nada a subir.
>
> | o que olhar | endereço |
> |---|---|
> | agente com dado, vínculo **cadastrado e ocioso** | `/agents/318924ed-78da-41ff-9536-bd4f372fc0c3?tab=insights` |
> | agente com **célula vazia real** e "sem delegação cadastrada" | `/agents/475a81a2-0106-49e3-b612-4f17305c7c7a?tab=insights` |
> | agente **inativo** | `/agents/4ab9739f-876a-4b7b-b953-7639df9701e7?tab=insights` |
> | `200` zerado (agente sem execução) | `/agents/69453038-9f1f-482e-b310-60653844ee0a?tab=insights` |
>
> Trocar de tema pelo botão no rodapé da barra lateral. O seletor 7d/30d/90d
> está no topo da aba.
>
> **Os seis pontos em que a tela contraria o artboard, para o olho ir direto:**
> o card "Onde o tempo foi" **sem barra**; "Média" onde o desenho diz "Mediana";
> "Fora do provedor" onde o desenho diz "Ferramentas"; a tabela de modelos
> **sem** a coluna "Cache lido", com a lacuna declarada no rodapé; o card de
> falhas em **grupos** em vez de tabela única; e o tracejado de "Acionado por"
> **sem** a frase sobre pedidos externos quando não houve nenhum.
>
> **O que só a semeadura alcança**, e é decisão do dono autorizá-la: os quatro
> cenários de delegação, e sobretudo o cenário em que os **dois lados
> divergem** — que é o que a D2 mais quer ver julgado.

- [ ] 6.1 Subir o painel contra o piloto e capturar a aba nos **quatro** cenários
      de delegação, nos **dois** esquemas — oito capturas.
- [ ] 6.2 Capturar os três estados de resposta: `404`, `200` zerado e agente
      **inativo**, nos dois esquemas.
- [ ] 6.3 Capturar a consulta em curso e a consulta falhada, com a API derrubada
      — é onde a #52 achou o defeito que a suíte verde não pegava.
- [ ] 6.4 Capturar o card de delegação no cenário em que os dois lados
      **divergem**, com a linha de resultado discriminado. **É a captura que mais
      provavelmente muda a decisão** (D2, risco registrado: a linha de detalhe
      pode engordar o card e desmanchar o ranking).
- [ ] 6.5 Capturar o quadro 4 do `Estados.dc.html` contra a tela real — os quatro
      estados de valor lado a lado — e o `0` de vínculo cadastrado e ocioso ao
      lado do tracejado de ausência de cadastro, que é o par mais fácil de
      confundir nesta aba.
- [ ] 6.6 Capturar o `TimeBreakdownCard` **sem a barra**, para o dono julgar se o
      card parece incompleto (D5, risco registrado).
- [ ] 6.7 Capturar as duas seções com nome de agente longo — a quebra de linha,
      que jsdom não calcula — e os seis `caveats` em largura de trabalho real
      (~1860px).
- [ ] 6.8 Levar ao dono, na mesma rodada, as **três** perguntas abertas do
      `design.md`: as quatro métricas servidas e não desenhadas (D10), a linha de
      resultado discriminado (D2) e o texto novo da nota mista (D8).
- [x] 6.9a **Primeira rodada, 26/09/2026 — a prosa sai dos dois quadros.** O dono
      apontou os parágrafos explicativos de `Dias da semana` e `Delegação`.
      Eram **quatro**, e não eram a mesma coisa: três eram texto de apoio da
      tela e **saíram** (junto com a prop `note` que carregava um deles, em vez
      de deixá-la sem uso); o quarto era o `caveat`
      `delegation-sides-are-not-mirrors`, que a **rota** emite, e **virou ícone
      com tooltip** no cabeçalho do card, por decisão do dono. A spec foi
      ajustada para admitir a forma sem afrouxar o contrato, e a régua nova está
      no `02`: *nem todo parágrafo é texto de tela — conferir se é prosa da
      implementação ou conteúdo que a rota manda antes de remover*. Captura
      `05-caveat-como-tooltip.png`. Suíte em **1381/1381**.

- [x] 6.9b **Segunda rodada, 26/09/2026 — o caveat do KPI `Taxa de falha`.** O
      dono apontou um rodapé que o artboard não desenha. Aplicada a régua da
      rodada anterior, o achado foi outro: `rejections-missing-from-executions`
      estava renderizado **duas vezes** na mesma aba, e a página do sistema já o
      renderiza **uma** vez só, no card de falhas. **Duplicação introduzida por
      esta change**, não requisito — saiu do KPI, continua ao lado das contagens
      de recusa. **Sem mudança de spec.** Efeito colateral: os quatro KPIs
      voltaram à mesma altura. Captura
      `06-kpis-taxa-de-falha-sem-caveat.png`. Suíte em **1382/1382**.

- [x] 6.9c **Terceira rodada, 26/09/2026 — três quadros, e duas reversões.**
      `Duração da task` perdeu os dois rodapés (prosa + `caveat`, que virou
      ícone); `Onde o tempo foi` **ganhou a régua empilhada e o total no
      cabeçalho** (reverte a **D5**, com a medição na mesa), voltou o nome
      **"Ferramentas"** (reverte a **D4**) e ficou com **uma** explicação, a do
      artboard; `Modelos` passou a ter **uma** linha de rodapé. Preservado em
      todas: o rótulo do total é "total das três" (a régua da D3 vale para a
      palavra), a barra só é desenhada com as três parcelas conhecidas, e o
      `caveat` do resíduo virou ícone — com "Ferramentas" de volta ele é o
      único aviso de que o resíduo não é só ferramentas. As duas reversões
      estão registradas **no `design.md`, ao lado das decisões originais**, e no
      `02`. Capturas `07-regua-escuro.png` e `08-regua-claro.png`. Suíte em
      **1380/1380**.

- [x] 6.9d **Quarta rodada, 26/09/2026 — a D6 contradizia a D10.** O dono
      apontou o rodapé do card de falhas. **O título não era defeito:** o
      protótipo tem dois, e a implementação seguia os dois — o que faltava era
      isso estar escrito. **O rodapé era**, e de princípio: a D6 criou um grupo
      para a recusa de entrada, e a D10 da mesma change proíbe criar elemento
      para métrica servida e não desenhada. A contradição passou por dez
      guardas verdes — **nenhum guarda pega contradição entre duas decisões**.
      A recusa de entrada foi para "servido e não desenhado"; `rejectedCount`
      continua no KPI, com o `caveat` de volta lá como ícone; a contagem de
      falhas foi para a direita do cabeçalho; `rejectionReasonLabels.ts` foi
      removido por ficar sem consumidor (convenção 2). Captura
      `09-falhas-sem-rodape.png`. Suíte em **1372/1372**.

- [x] 6.9 **Aguardar o julgamento. Cumprido em QUATRO rodadas**, 26/09/2026,
      encerradas pelo dono com *"desenvolvimento concluído"*. Cada uma está
      registrada acima e no `02`, com o que mudou e por quê.

      **A conferência não aconteceu como as tarefas 6.1–6.7 a previam, e vale
      registrar a diferença:** elas previam um LOTE de capturas entregue de uma
      vez, e o dono julgando o lote. O que aconteceu foi o dono apontando a tela
      diretamente, quadro a quadro, com o agente produzindo o ajuste e a captura
      do resultado a cada rodada. **Foi mais barato e pegou mais**: as quatro
      rodadas acharam sete achados, e o mais grave — a contradição entre a D6 e
      a D10 — não estava em nenhuma das capturas previstas, porque ele não era
      de fidelidade visual, era de princípio.

      **O que as 6.1–6.7 previam e NÃO foi produzido, com a razão:**
      os quatro cenários de delegação e o cenário de divergência entre os dois
      lados (6.1 e 6.4) pedem **semeadura de `delegation_outcomes`** no banco de
      dev, que não os tem — e semear o banco do dono é decisão dele, não pedida
      nesta change; o `404` e o estado de consulta sem resposta **da aba** (6.2 e
      6.3) não são alcançáveis pela navegação normal, porque a **página** falha
      antes, em `useAgentQuery` — os dois têm guarda, e a D17 já registra por
      que o `404` só aparece se as duas rotas discordarem.

      **Permanecem como item aberto**, na lista abaixo, para a primeira vez que
      houver delegação no ambiente de conferência.

## 7. As divergências com o protótipo — registro (convenções 9 e 17)

Cada uma já está decidida no `design.md`. Esta seção é o **registro**, e ele
precede o archive.

- [x] 7.1 Registrar no `02` as **sete** divergências, cada uma com causa, o que a
      tela faz no lugar e o gatilho para voltar: D2 (resultado discriminado
      acrescentado), D3 (mediana → média), D4 (resíduo não é "Ferramentas"), D5
      (barra empilhada não desenhada), D6 (a recusa que sai do subtítulo), D9
      (tabela única vira grupos), D14 (a segunda frase do tracejado).
- [x] 7.2 Registrar a **armadilha de `maxDepthAtWhichAgentRan`** por escrito
      (D10): se algum dia for desenhada, o rótulo diz *"a maior profundidade em
      que este agente executou"* — posição na cadeia, nunca tamanho dela, nunca
      o rótulo do escopo do sistema.
- [x] 7.3 Registrar o que é **servido e não desenhado**, com o gatilho de cada
      um: tokens de embedding de busca e consumo por provedor, profundidade,
      tasks sem estado terminal.

## 8. Issues (convenção 23)

O que não tem issue não existe depois do archive.

- [x] 8.1 **Aberta: [#80](https://github.com/aldovrando-oliveira/buteco-agentes/issues/80)** — a página do sistema não conhece o que a #51 entregou.
      `errors.rejectedAtEntryCount`, `errors.rejectionsByReason` e
      `errors.rejectionRegime` são servidos por `apps/api` desde 26/09 e
      `src/features/insights/types/systemInsights.ts` não tem nenhum dos três —
      a contagem e os motivos de recusa de entrada não chegam à tela do sistema,
      que é a lacuna **L1** que a #51 existia para fechar. Incluir a entrada
      morta `rejection-reason-not-collected` no mapa de `caveatLabels.ts`, que é
      a mesma causa. App: `apps/frontend`. Gatilho: imediato.
- [x] 8.2 **Aberta: [#81](https://github.com/aldovrando-oliveira/buteco-agentes/issues/81)** — o tempo de provedor **por task** não é servido, e sem
      ele a barra empilhada de "Onde o tempo foi" não pode ser composta (D5). A
      rota precisaria de `avg(Σ DurationMs por task)` e das três parcelas sobre a
      **mesma** população — hoje são três populações diferentes. App:
      `apps/api`. Gatilho: primeiro pedido de ver a repartição do tempo.
- [x] 8.3 **Registrado na #67** (comentário de 26/09/2026) o que esta change mostrou, sem decidi-la: se
      tasks, tokens por task e duração p95 por agente ficaram legíveis nesta aba
      a ponto de as três colunas não precisarem entrar no ranking do sistema. Se
      o caminho for "só na aba", a #67 fecha sem código — **e a decisão é do
      dono**.
- [x] 8.4 Conferir que toda issue nova nasceu **antes** do archive, e que nenhuma
      lacuna desta change existe só no `design.md`. **Conferido:** #80 e #81
      abertas em 26/09, com a change **ativa** e o archive ainda por fazer; o
      gatilho da #67 registrado lá; as demais lacunas apontam para #66 e #67,
      que já existiam. Nenhuma das sete divergências vive só no `design.md` —
      todas estão na tabela do `02`.

## 9. Registro e fechamento

- [x] 9.1 Escrever a seção do `02-HISTORICO_E_STATUS.md`: a leitura dos
      artboards, o mapa elemento → campo, as sete divergências, as três
      mutações da 5.3, as rodadas da conferência manual e os itens abertos com
      gatilho e número de issue.
- [x] 9.2 **Fechar a vigésima primeira medição da convenção 18** — comparar a
      projeção do `design.md` com o medido, nas dimensões declaradas lá, **sem
      revisar a projeção**. Contar `#### Scenario:` nos dois arquivos de delta,
      separando os novos dos copiados pelo bloco `MODIFIED`, e usar `git diff -w`
      para o modificado. Medir a proporção comentário:lógica **por tipo de
      arquivo**, nos quatro de registro e nos nove de mecanismo, separados — foi
      a régua que a vigésima produziu.
- [x] 9.3 Registrar as baselines de fechamento ao lado das de abertura, com o
      `podman ps` colado, e declarar quais suítes **não** rodaram e por quê.
- [x] 9.4 Atualizar o `CHANGELOG.md` e rodar `scripts/check-docs.py`.
- [x] 9.5 **Archive antes do push** (convenção 24). Nenhum push e nenhum PR com a
      change ativa.
      **O archive foi MANUAL, por decisão do dono (26/09/2026):** ele validou
      visualmente em quatro rodadas antes de autorizar — e a decisão se provou
      certa, porque cada uma das quatro mudou código. Arquivar antes teria
      obrigado a reabrir a change quatro vezes.
      **Executado em 26/09/2026**, com as delta specs sincronizadas e a
      medição da convenção 18 refeita depois do julgamento.
- [ ] 9.6 **(pós-archive)** Abrir **um** PR com `Closes #53` no corpo, o que move
      a issue para `In review`. A **#53 já está em `In progress`** — movida em
      26/09/2026, na abertura desta change.
- [ ] 9.7 **(pós-archive)** Registrar na #53 o fechamento: as capturas da
      conferência manual, o resultado da vigésima primeira medição, o estado de
      cada uma das sete divergências e o que a aba mostrou sobre a #67.

> **Nada é commitado sem autorização explícita do dono.** A árvore fica pronta e
> o que está pendente de commit é declarado.

## 10. Conferência de escopo — a lista fechada

Montada por **leitura do repositório** em 26/09/2026, **não** por analogia com a
change da #52. Antes de fechar, confirmar com `git status` que o diff contém
**apenas** o que está na primeira tabela.

**Pode ser tocado:**

| caminho | o que muda |
|---|---|
| `apps/frontend/src/features/insights/types/agentInsights.ts` | novo |
| `apps/frontend/src/features/insights/api/useAgentInsights.ts` (+ teste) | novo |
| `apps/frontend/src/features/insights/api/insightsApi.ts` (+ teste) | `getAgentInsights` |
| `apps/frontend/src/features/insights/test/agentInsightsFixture.ts` | novo — o duplo |
| `apps/frontend/src/features/insights/utils/delegationRows.ts` (+ teste) | novo |
| `apps/frontend/src/features/insights/utils/delegationOutcomeLabels.ts` (+ teste) | novo |
| `apps/frontend/src/features/insights/utils/rejectionReasonLabels.ts` (+ teste) | novo |
| `apps/frontend/src/features/insights/utils/caveatLabels.ts` (+ teste) | 2 códigos, posição por superfície |
| `apps/frontend/src/features/insights/components/KpiCard.tsx` | extraído |
| `apps/frontend/src/features/insights/components/InsightsKpiGrid.tsx` | passa a importar `KpiCard` |
| `apps/frontend/src/features/insights/components/WeekdayActivityCard.tsx` (+ teste) | prop `note` opcional |
| `apps/frontend/src/features/insights/components/Agent*.tsx`, `TimeBreakdownCard.tsx`, `TaskDurationCard.tsx` (+ testes) | novos |
| `apps/frontend/src/features/agents/pages/AgentDetailPage.tsx` (+ teste) | a quinta aba |
| `openspec/changes/insights-aba-do-agente/**` | os artefatos desta change |
| `02-HISTORICO_E_STATUS.md` | seção 9.1 |
| `CHANGELOG.md` | a entrada da change |

**Não pode ser tocado, e a razão de cada um:**

| caminho | razão |
|---|---|
| `apps/api/**` | a rota já serve o contrato desta aba; achado de UI é sequenciado, não corrigido aqui (convenção 1). **Lido** nas tarefas 0.5, 0.6 e 2.6 |
| `apps/workers/**` | não participa desta etapa; os dois vocabulários são **copiados por leitura**, nunca importados |
| `apps/inbox/**` | não participa; nenhum número desta aba vem de lá |
| `libs/**` | nada a compartilhar; é código de backend |
| `deploy/`, `stack/`, `tests/CrossApp*`, `tests/Inbox*` | nenhuma migração, nenhum contrato entre apps |
| `apps/frontend/deploy/nginx.conf` | **verificado na linha 59**: `agents` e `insights` já estão no bloco do `api`, e a aba é parâmetro de consulta sobre `/agents/{id}`, coberta pelo `rewrite ^ /index.html` do `Sec-Fetch-Mode: navigate` |
| `apps/frontend/src/app/routes.tsx` | a aba **não** é rota nova |
| `apps/frontend/package.json` | nenhuma dependência nova |
| `apps/frontend/src/theme.ts` | a escala de calor já existe desde a #52, e esta aba **não** tem mapa de calor |
| `apps/frontend/src/components/**` | nenhum componente transversal entra; o que foi extraído (`KpiCard`) fica **dentro** da feature, com dois consumidores da mesma feature |
| `apps/frontend/src/features/insights/pages/SystemInsightsPage.tsx` | a página do sistema não muda de comportamento; o que ela precisa está na issue 8.1 |
| `apps/frontend/src/features/insights/types/systemInsights.ts` | idem — os três campos da #51 são a issue 8.1, não esta change |
| `apps/frontend/src/features/insights/components/{PeriodHeatmapCard,DailyTasksCard,AgentConsumptionCard,ProviderConsumptionCard,ConversationModelsCard,FailuresCard,FailureReasonsCard,NonTerminalBanner}.tsx` | só a página do sistema os usa; esta aba não tem mapa de calor nem série diária |
| `apps/frontend/src/features/agents/**` fora de `pages/AgentDetailPage*` | o catálogo é **consumido** por prop, nunca modificado |
| `apps/frontend/src/features/{channels,inventory,knowledge-bases,mcp-servers,sessions,auth}/**` | nenhuma relação com esta aba |
| `docs/**`, `01-ARQUITETURA_E_CONVENCOES.md` | esta change **cumpre** convenções, não as altera |
| `openspec/specs/**` | main specs só mudam no archive, nunca no apply |
| `openspec/changes/archive/**` | registro do que se decidiu então; não se edita |
