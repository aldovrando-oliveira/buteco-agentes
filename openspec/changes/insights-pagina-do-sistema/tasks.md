## 0. A dependência, antes de qualquer código

- [ ] 0.1 **Conferir que a #65 fechou** — a série diária precisa emitir `0` para
      dia medido e vazio. Chamar a rota com uma janela que contenha um dia
      medido e sem task e **ver o ponto com `taskCount: 0` na `dailySeries`**.
      Enquanto ela não fechar, a tela não tem como distinguir "não medido" de
      "medido e vazio" lendo a série, e implementar contra a rota incompleta
      produz uma tela que parece certa e mente sobre dois estados (D2).
- [ ] 0.2 Confirmar que o XML doc de `DailyInsightPoint` foi corrigido junto —
      é parte do escopo da #65, e é o que impede a próxima leitura de repetir o
      engano.

## 1. Contrato da rota e cliente da consulta

Tudo nesta change roda em **`apps/frontend`**. Nenhuma tarefa toca outro app.
A correção que a tarefa 0.1 confere é **change própria em `apps/api`** e não
faz parte desta.

- [ ] 1.1 Criar `src/features/insights/types/systemInsights.ts` com o contrato da
      resposta de `GET /insights/system` em `camelCase`, transcrito de
      `SystemInsightsResponse.cs` e conferido contra o corpo real registrado no
      `design.md`. Todo campo numérico anulável entra como `number | null` — **não**
      como `number` com default, e **nenhum** campo recebe `?? 0` no tipo nem na
      desserialização.
- [ ] 1.2 Criar `src/features/insights/api/insightsApi.ts` com `request<T>` e
      `ApiError` **próprios da feature**, copiados do idioma de `agentsApi.ts`
      (base `VITE_API_BASE_URL ?? 'http://localhost:5017'`, `Authorization` do
      token, `401` limpando o token e redirecionando). Sem cliente HTTP
      compartilhado — é regra escrita da casa.
- [ ] 1.3 Acrescentar `getSystemInsights(from, to)` montando a query string com
      os **dois** limites obrigatórios, no idioma de `periodQuery` de
      `sessionsApi.ts`.
- [ ] 1.4 Escrever `insightsApi.test.ts`: a URL montada com os dois limites; o
      `401` limpando o token; o `ValidationProblem` de janela chegando como
      `ApiError` com o corpo preservado.
- [ ] 1.5 Criar `src/features/insights/utils/insightsWindow.ts` — período
      (`7d`/`30d`/`90d`) + instante → `{ from, to }` em ISO UTC, **rolante em
      instantes**, com "agora" como parâmetro. Copiar a razão de
      `activityWindow.ts` no comentário e apontar para lá.
- [ ] 1.6 Escrever `insightsWindow.test.ts`: os três períodos; "agora" como
      parâmetro produzindo janela determinística; saída sempre em UTC com `Z`.
- [ ] 1.7 Criar `src/features/insights/api/useSystemInsights.ts` — `useQuery` com
      chave `['insights', 'system', <período>]` e a janela calculada **dentro do
      `queryFn`**. Comentar por que a janela não entra na chave, apontando para
      `useSessions.ts`.
- [ ] 1.8 Escrever `useSystemInsights.test.ts`: uma consulta por período; renders
      repetidos sem troca de período não geram consulta nova; nova tentativa
      recalcula a janela.
- [ ] 1.9 Criar `src/features/insights/test/systemInsightsFixture.ts` — construtor
      da resposta agregada com padrões razoáveis e sobrescrita **por bloco**. É o
      duplo único desta change (projeção da convenção 18); ele é o que torna
      barato escrever os cinquenta cenários que diferem num campo cada.

## 2. Os utilitários puros que carregam a gramática

- [ ] 2.1 Criar `utils/metricState.ts`: dado `number | null | undefined` mais o
      estado da consulta, devolver um dos quatro estados — valor, vazio,
      desconhecido, zero — e a formatação em `pt-BR` (número, token abreviado,
      duração em segundos, percentual). **Nenhum `?? 0`, `|| 0` ou `Number(x)`**
      em nenhum caminho.
- [ ] 2.2 Escrever `metricState.test.ts` com os guardas **negativos**: nulo e
      indefinido **não** produzem `'0'`; soma de parcelas todas nulas **não**
      produz `'0'`; percentual com denominador zero **não** produz `'0%'` nem
      `NaN`; zero medido produz o estado de zero e nunca o de vazio.
- [ ] 2.3 Criar `utils/measuredDays.ts`: janela + `dailySeries` + fuso da
      resposta → o conjunto de dias da janela e `dayState(day)` devolvendo
      medido / zero medido / não medido **lendo só a série** — dia presente com
      contagem > 0 é medido, presente com `0` é zero medido, ausente é não
      medido. Conversão de instante para `YYYY-MM-DD` por
      `Intl.DateTimeFormat('sv-SE', { timeZone })`, **nunca** pelo fuso do
      navegador. **O módulo não recebe `regimes` e não o consulta** (D2): o
      regime serve ao texto "medindo desde", não a decidir célula.
- [ ] 2.4 Escrever `measuredDays.test.ts`: dia presente com contagem; dia
      presente com `0` vira zero medido; dia ausente vira não medido; e o
      negativo — **nenhum** dia ausente da série devolve contagem. Incluir um
      caso com fuso de navegador diferente do da resposta, afirmando que o dia
      não desloca. Incluir o guarda de acoplamento: a função assinada **sem**
      `regimes` é o que impede a reconstrução voltar por descuido.
- [ ] 2.5 Acrescentar a `measuredDays.ts` a cobertura por dia da semana: quais
      dias da semana **ocorrem entre os dias da série**. `byWeekday` tem o mesmo
      defeito da série e a spec da change A **não** o cobre — esta é a única
      reconstrução que sobra no cliente, e a fonte dela é a série, não o regime.
- [ ] 2.6 Escrever o caso correspondente: série de dois dias cobre dois dias da
      semana, e os outros cinco saem como não medidos — **nenhum** deles como
      zero.
- [ ] 2.7 Criar `utils/heatScale.ts`: quantização linear em cinco faixas sobre
      `[1, máximo medido]`, com passo próprio para o zero medido. Função pura.
- [ ] 2.8 Escrever `heatScale.test.ts`: série normal distribui nas cinco faixas;
      série de valor único não quebra; máximo 1 não divide por zero; o zero
      medido nunca recebe passo de intensidade.
- [ ] 2.9 Criar `utils/failurePhaseLabels.ts` — as sete fases de
      `ExecutionMetricsValues.FailurePhase` mais os resultados de indexação,
      traduzidos para rótulo de operador; valor desconhecido devolve
      apresentação **neutra** com o próprio valor.
- [ ] 2.10 Escrever `failurePhaseLabels.test.ts`: cada fase conhecida; e o
      desconhecido devolvendo o valor cru, **não** o rótulo de outra fase.
- [ ] 2.11 Criar `utils/caveatLabels.ts` — o mapa **fechado** dos cinco códigos
      para texto, mais a posição de cada um (qual número ele limita), conforme a
      tabela da D12. Código desconhecido devolve o próprio código para exibição
      visível.
- [ ] 2.12 Escrever `caveatLabels.test.ts`: os cinco códigos; o desconhecido
      aparecendo em vez de sumir; e `residual-is-not-only-tools` classificado
      como **sem posição nesta página**.

## 3. Tema: a escala de calor por esquema

- [ ] 3.1 Acrescentar `--buteco-heat-0` a `--buteco-heat-5` ao
      `cssVariablesResolver` de `src/theme.ts`, nos **dois** blocos, com os
      valores da tabela da D7 — todos já existentes em `butecoBlue`, `gray` e
      `dark`. Comentar ao lado, no idioma de `--buteco-surface-subtle`, que o
      papel troca de ponta da escala entre os esquemas.
- [ ] 3.2 Acrescentar a `src/theme.test.ts` as asserções de contrato: os seis
      passos existem nos dois esquemas, e cada um aponta para um tom da paleta —
      nenhum literal novo.

## 4. Componentes de apresentação

Regra da casa e desta change: **a página busca e repassa; componente
apresentacional não importa hook de query**. Todos recebem props.

- [ ] 4.1 `components/MetricValue.tsx` — renderiza os quatro estados de
      `metricState`, com o travessão sempre acompanhado da razão.
- [ ] 4.2 `MetricValue.test.tsx`, com os guardas negativos: para origem nula,
      afirmar que o texto renderizado **não** contém `0`.
- [ ] 4.3 `components/DeclaredGap.tsx` — o estado de lacuna declarada do quadro 6
      de `Estados.dc.html`: moldura tracejada, o nome do que falta e o texto do
      motivo. Apresentação **distinta** do travessão.
- [ ] 4.4 `DeclaredGap.test.tsx`: renderiza o nome e o motivo; e o negativo —
      **não** renderiza número nem travessão.
- [ ] 4.5 `components/PeriodPicker.tsx` — 7d / 30d / 90d, com 30d como padrão.
- [ ] 4.6 `PeriodPicker.test.tsx`: aciona o callback com o período escolhido; o
      selecionado é o único marcado.
- [ ] 4.7 `components/InsightsKpiGrid.tsx` — os seis cards do protótipo, na ordem
      de `Main.dc.html`. O de chamadas ao provedor soma
      `byModel[].callCount` e traz a lacuna declarada de turno × compactação
      (L2); o de duração diz **"média"** e não "mediana" (L5, D9).
- [ ] 4.8 `InsightsKpiGrid.test.tsx`: cada card com a sua fonte; o total de
      chamadas somado das linhas de modelo; tokens de conversa com uma parcela
      nula ficando vazio e **não** zero; a lacuna de turno × compactação
      presente; e o rótulo "média" — afirmando que a palavra "mediana" **não**
      aparece.
- [ ] 4.9 `components/WeekdayActivityCard.tsx` — as sete linhas, a marcação do
      pico, e a célula vazia para dia da semana fora da faixa medida.
- [ ] 4.10 `WeekdayActivityCard.test.tsx`: dia com atividade; dia coberto e
      zerado; dia **não** coberto saindo vazio, sem `0`; sem pico declarado,
      nenhuma marcação.
- [ ] 4.11 `components/PeriodHeatmapCard.tsx` — a grade de sete linhas por
      semana, os quatro estados de célula, a legenda "menos … mais" lendo as
      variáveis do tema, e a nota de fuso local.
- [ ] 4.12 `PeriodHeatmapCard.test.tsx`: a contagem de células da janela; dia não
      medido com marcador próprio e **sem** passo de intensidade; zero medido com
      o passo de zero; posição fora da janela sem célula.
- [ ] 4.13 `components/DailyTasksCard.tsx` — a linha em SVG inline, com o trecho
      não medido como faixa hachurada e a linha **quebrando** ali em vez de cair
      a zero. Rótulo acessível na figura.
- [ ] 4.14 `DailyTasksCard.test.tsx`: a série medida vira pontos; o prefixo não
      medido vira faixa e **não** entra na linha; um dia medido com zero entra
      como ponto em zero.
- [ ] 4.15 `components/ProviderConsumptionCard.tsx` — Provedor / Conversa /
      Embedding / Total, com célula vazia onde a origem é nula, o total somando
      só o conhecido, e o texto que declara o significado da célula vazia.
- [ ] 4.16 `ProviderConsumptionCard.test.tsx`: célula vazia sem `0`; total com
      uma parcela nula; total com todas nulas ficando vazio; o texto presente.
- [ ] 4.17 `components/ConversationModelsCard.tsx` — Modelo / Provedor /
      Chamadas / Tokens, ordenado por tokens, com a nota do protótipo sobre a
      ordem por número de chamadas ser outra, e a lacuna declarada da coluna
      Cache lido (L3).
- [ ] 4.18 `ConversationModelsCard.test.tsx`: a ordenação; `totalTokens` nulo
      ficando vazio e `callCount` zero saindo como zero medido — os dois na mesma
      linha, que é o par que separa os dois estados; a lacuna de cache presente.
- [ ] 4.19 `components/AgentConsumptionCard.tsx` — Agente / Tokens / Falhas, com
      o nome vindo do catálogo, o identificador abreviado quando o agente não
      está no catálogo, link para `/agents/{id}`, ordenação por tokens, e a
      lacuna declarada das três colunas sem fonte (L4).
- [ ] 4.20 `AgentConsumptionCard.test.tsx`: nome cruzado; agente fora do catálogo
      mantendo a linha com o identificador; o link apontando para o detalhe; a
      lacuna nomeando as três colunas; e o negativo — **nenhuma** das três
      aparece preenchida.
- [ ] 4.21 `components/FailuresCard.tsx` — as duas contagens separadas, o texto
      do protótipo sobre a diferença entre recusa e falha (literal), percentual
      **só** na falha, e o texto do caveat no lugar do percentual da recusa (D10).
- [ ] 4.22 `FailuresCard.test.tsx`: as duas contagens; a falha com percentual; a
      recusa **sem** percentual e com o caveat; total de tasks zero não
      produzindo percentual nem `NaN`.
- [ ] 4.23 `components/FailureReasonsCard.tsx` — fases de falha e falhas de
      indexação com rótulo de operador, fase desconhecida de forma neutra, e as
      recusas como lacuna declarada com a contagem e o caveat (L1).
- [ ] 4.24 `FailureReasonsCard.test.tsx`: cada fase com o seu rótulo; a
      desconhecida com o valor cru; a recusa como lacuna **sem causa nomeada**;
      e nenhum motivo no período dizendo o zero medido por extenso.
- [ ] 4.25 `components/NonTerminalBanner.tsx` — as duas populações separadas com
      as suas causas, o texto de `point-in-time-only`, **sem** afirmação de idade
      e **sem** link, e ausente quando as duas contagens são zero (D11).
- [ ] 4.26 `NonTerminalBanner.test.tsx`: as duas populações; o texto do instante;
      o negativo — o banner **não** menciona prazo nem oferece link; e duas
      contagens zero não renderizando banner nenhum.

## 5. A página e a navegação

- [ ] 5.1 Criar `pages/SystemInsightsPage.tsx`: detém `useSystemInsights` e
      `useAgentsQuery`, decide o período, calcula os dias medidos uma vez e
      repassa tudo por prop. Cada consulta com o seu carregamento, o seu erro e a
      sua nova tentativa — **nada** de `isLoading || outraQuery.isLoading`.
- [ ] 5.2 Montar o cabeçalho: a janela pedida em datas locais, e o "medindo
      desde" **por regime**, junto do grupo que cada regime mede — nunca um texto
      único para a página.
- [ ] 5.3 Escrever `SystemInsightsPage.test.tsx`: uma requisição à rota agregada
      por período; o cabeçalho com os dois regimes; o catálogo lento não segurando
      os números; a falha do catálogo não derrubando a página; a falha da rota
      agregada apresentando travessão com razão e nova tentativa, e **nenhum** `0`.
- [ ] 5.4 Acrescentar a rota `insights` a `src/app/routes.tsx`, dentro do grupo
      protegido e da casca.
- [ ] 5.5 Acrescentar o item `Insights` a `navItems` em
      `src/components/layout/AppShell.tsx`, com ícone do `lucide-react`.
- [ ] 5.6 Acrescentar os casos a `router.test.tsx` e `AppShell.test.tsx`: a rota
      resolve para a página; o item aparece com rótulo e ícone e fica ativo na
      rota.

## 6. Verificação de suíte

- [ ] 6.1 Rodar `npm test -- --run` em `apps/frontend` e comparar com a baseline
      do `design.md` (**927/927** em 84 arquivos, medida em 23/09/2026 22:26 -03
      sobre `0b189fa`). Comparar **por nome de teste**, nunca só pelo total.
- [ ] 6.2 Rodar `npm run lint` e a checagem de tipos de `apps/frontend`.
- [ ] 6.3 Confirmar que as suítes de `apps/api` (**391/391**), `apps/workers`
      (**386/386**) e `apps/inbox` (**203/203**) **não foram tocadas** — nenhum
      arquivo desta change vive fora de `apps/frontend`. Registrar as baselines do
      `design.md` sem rodá-las de novo, salvo se algum arquivo fora de
      `apps/frontend` tiver mudado. Se alguma precisar rodar, rodar **uma por
      vez**: a baseline do `design.md` registra uma reprovação por contenção em
      `apps/inbox` que sumiu na rodada isolada.
- [ ] 6.4 `openspec validate --all` verde.

## 7. Conferência manual — do dono, iterativa

O agente **produz os estados**; o julgamento é do dono (convenção 14). Precedente:
a `frontend-mensagem-recusa-ciclo` capturou as telas e o dono julgou.

- [ ] 7.1 Subir o painel contra o piloto e capturar a página em **7d, 30d e 90d**,
      nos **dois esquemas** de cor — seis capturas.
- [ ] 7.2 Capturar a **inversão da escala do mapa de calor** lado a lado: a suíte
      afirma que as variáveis existem, não que a rampa cresce no sentido certo.
- [ ] 7.3 Capturar o contraste entre a faixa hachurada e a célula de zero medido
      **no tema claro**, que é onde a distinção é mais fácil de perder.
- [ ] 7.4 Produzir os seis quadros de `Estados.dc.html` contra a tela real:
      coleta recém-iniciada, período medido sem uso, consulta sem resposta, os
      quatro estados de valor, provedor que não reporta, e a lacuna declarada.
- [ ] 7.5 Capturar as duas tabelas com nome de agente e de modelo longos — a
      quebra de linha, que jsdom não calcula.
- [ ] 7.6 Capturar os cinco textos de caveat em largura de trabalho real (~1860px).
- [ ] 7.7 Entregar as capturas ao dono e **aguardar o julgamento**. Cada correção
      muda o que fica visível: repetir 7.1 a 7.6 depois de qualquer ajuste visual.

## 8. Registro e fechamento

- [ ] 8.1 Escrever o `02-HISTORICO_E_STATUS.md` com os itens abertos que esta
      change cria, cada um com gatilho, posição **e o número da issue** — o `02`
      narra, a issue é o que sobrevive ao archive (convenção 23):
      **(a)** o motivo das recusas — M29, **#51** — change de coleta, e a ordem
      foi **invertida**: nasce **depois** desta, sabendo o formato que o card
      pediu;
      **(b)** separação turno × compactação (L2), cache lido por modelo (L3) e
      mediana da duração (L5) — **#66**, uma mudança só em `apps/api`, gatilho no
      primeiro pedido do dono; a L3 é divergência com **decisão registrada**
      (`02:4994`), não só com o protótipo;
      **(c)** as três colunas por agente (L4) — **#67**, gatilho na etapa 5
      (#53), que pode torná-la redundante;
      **(d)** `byWeekday` tem o mesmo defeito que a #65 corrige na série diária,
      e a spec da change A não o cobre — gatilho: a change da #65 decide se
      estende o recorte a M6 ou se a cobertura fica do cliente (D2);
      **(e)** os campos servidos e não desenhados — resíduo, tempo de fila,
      duração de chamada ao provedor, profundidade de delegação, pares de
      delegação, falhas de indexação como bloco e cache global — escolha de
      produto, sem gatilho;
      **(f)** subir `apps/api` em macOS exige `TZ=America/Sao_Paulo` no ambiente,
      senão a checagem de boot reprova com `'Brazil/East'`;
      **(g)** link do banner de não-terminais — gatilho: a primeira tela de tasks;
      **(h)** período na URL — gatilho: o primeiro pedido de compartilhar link.
- [ ] 8.2 Registrar os **dois gatilhos já cumpridos** por esta change: a resposta
      levou **2,24 s** contra o banco de dev, disparando o gatilho de latência da
      D6 da etapa 3 (com a ressalva de que a medição de dev inclui subida a frio);
      e a tela foi exercitada contra dev, que é o ambiente em que a configuração
      de regime não corresponde à medição real — o caso que a D8 da etapa 3
      nomeou sem resolver.
- [ ] 8.3 Fechar a **décima sexta medição da convenção 18**: comparar a projeção
      do `design.md` com o medido, nas três colunas separadas — casos escritos à
      mão, **duplo**, e gerado (que deve ser 0). Contar `#### Scenario:` nos
      arquivos de delta e usar `git diff -w` para o modificado. **Não revisar a
      projeção**; só comparar.
- [ ] 8.4 Atualizar o `CHANGELOG.md` e rodar `scripts/check-docs.py`.
- [ ] 8.5 Abrir o PR referenciando a **#52** no corpo, e conferir que o
      `proposal.md` abre com `**Issue:** #52` (convenção 23).
- [ ] 8.6 Registrar na **#52** o fechamento: as capturas da conferência manual,
      o resultado da décima sexta medição, e o estado de cada uma das cinco
      lacunas na tela entregue.

## 9. Conferência de escopo — a lista fechada

Montada por **leitura do repositório** em 23/09/2026, não por analogia. Antes de
fechar, confirmar com `git status` que o diff contém **apenas** o que está na
primeira coluna.

**Pode ser tocado:**

| caminho | o que muda |
|---|---|
| `apps/frontend/src/features/insights/**` | tudo novo |
| `apps/frontend/src/app/routes.tsx` | rota `insights` |
| `apps/frontend/src/app/router.test.tsx` | caso da rota nova |
| `apps/frontend/src/components/layout/AppShell.tsx` | item de navegação |
| `apps/frontend/src/components/layout/AppShell.test.tsx` | caso do item novo |
| `apps/frontend/src/theme.ts` | seis variáveis de calor, nos dois esquemas |
| `apps/frontend/src/theme.test.ts` | contrato das seis variáveis |
| `openspec/changes/insights-pagina-do-sistema/**` | os artefatos desta change |
| `02-HISTORICO_E_STATUS.md` | itens 8.1 e 8.2 |
| `CHANGELOG.md` | a entrada da change |

**Não pode ser tocado, e a razão de cada um:**

| caminho | razão |
|---|---|
| `apps/api/**` | a rota já serve o contrato desta tela; achado de UI é sequenciado, não corrigido aqui (convenção 1) |
| `apps/workers/**` | não participa desta etapa |
| `apps/inbox/**` | não participa; nenhum número desta página vem de lá |
| `libs/ProviderCatalog*` | nada a compartilhar; é código de backend |
| `tests/CrossAppTaskStoreCompatibility.Tests`, `tests/InboxOrchestratorRoundTrip.Tests` | contratos entre apps de backend, intocados |
| `deploy/migrate` | nenhuma migração |
| `apps/frontend/deploy/nginx.conf` | **verificado na linha 59**: o prefixo `insights` já está no bloco do `api`, e a rota de página é coberta pelo `rewrite ^ /index.html` do `Sec-Fetch-Mode: navigate` |
| `apps/frontend/package.json` | nenhuma dependência nova (D13) |
| `apps/frontend/src/components/**` fora de `layout/` | nenhum componente compartilhado entra; a etapa 5 é consumidor **previsto**, não observado (convenção 2, D14) |
| `apps/frontend/src/utils/`, `src/hooks/` | os utilitários desta tela são da feature, não transversais |
| `apps/frontend/src/features/agents/**` | só **consumida** (`useAgentsQuery`), nunca modificada |
| `apps/frontend/src/features/{channels,inventory,knowledge-bases,mcp-servers,sessions,auth}/**` | nenhuma relação com esta tela |
| `apps/frontend/src/auth/` | o token entra pelo módulo fino que cada `request<T>` já importa |
| `docs/conventions.md` e os demais `docs/` | esta change **cumpre** convenções, não as altera |
| `openspec/specs/**` | main specs só mudam no archive, nunca no apply |
| `openspec/changes/archive/**` | registro do que se decidiu então; não se edita (precedente da D10 da etapa 3) |
