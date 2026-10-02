# Design — o período entre as duas telas de Insights (#85)

## Context

**O estado no `HEAD` desta branch (`515883d`), lido no código e não de memória.**

| o quê | onde | o que faz hoje |
|---|---|---|
| o período da página do sistema | `SystemInsightsPage.tsx:146` | `useState<InsightsPeriod>(DEFAULT_INSIGHTS_PERIOD)` |
| o período da aba do agente | `AgentInsightsTab.tsx:150` | idem |
| a janela | `insightsWindow.ts:42` | `insightsWindow(period, now)` — *"agora" é parâmetro*, nada de `new Date()` lá dentro |
| o cálculo da janela | `useSystemInsights.ts:22`, `useAgentInsights.ts:35` | dentro do `queryFn`, com `new Date()` no instante da consulta |
| a chave de cache | idem | `['insights','system',period]` e `['insights','agent',agentId,period]` — **o nome do período, nunca os limites** |
| a aba no endereço | `AgentDetailPage.tsx:38-45,60-61` | `parseTab(searchParams.get('tab'))`, por `useSearchParams` |
| o escritor da aba | `AgentDetailPage.tsx:76` | `setSearchParams(next === OVERVIEW_TAB ? {} : { tab: next })` |
| a passagem | `AgentConsumptionCard.tsx:239` | `to={`/agents/${linha.agentId}?tab=insights`}` — **o único sítio de link entre as duas telas** |
| o seletor | `PeriodPicker.tsx:20` | `SegmentedControl` sobre `INSIGHTS_PERIODS = ['7d','30d','90d']` |

**A premissa central do enunciado confere: `parseTab` lê de `useSearchParams`.** O
caminho da URL está aberto, e a casa já tem o molde — o comentário de
`AgentDetailPage.tsx:34-37` declara o contrato *"ausência do parâmetro é a forma
canônica, e valor desconhecido cai nela também — sem reescrever o endereço, que só
poluiria o histórico"*.

### Três afirmações do enunciado e da issue que a leitura do código corrigiu

Convenção 6, e as três são da forma mais cara: item aberto lido depois por quem
não tem o contexto de quem o escreveu.

1. **Não existe `TimeProvider` nesta tela, nem em `apps/frontend`.** O enunciado
   pede *"conferir como a tela obtém o agora, e se há duplo de teste para ele"*. A
   resposta medida: `grep -rn "TimeProvider\|useNow\|nowProvider"` em `src/` devolve
   **zero** sítios — o "agora" é `new Date()` em quatro lugares
   (`useSystemInsights.ts:22`, `useAgentInsights.ts:35`,
   `SystemInsightsPage.tsx:180`, `AgentInsightsTab.tsx:175`). O duplo da casa é
   `vi.setSystemTime`, com precedente em `useSessions.test.ts:129` e
   `InventoryPage.test.tsx:525`, e **nenhum teste da feature `insights` o usa**. A
   consequência disso para o desenho está na **D8**, e ela é a razão pela qual a
   decisão da **D2** sai barata em teste.
2. **A linha que a issue cita para a página do sistema está defasada.** A #85 diz
   `SystemInsightsPage.tsx:110`; hoje é **`:146`**. O arquivo cresceu na
   `insights-sistema-motivo-da-recusa`, que entrou depois de a issue ser escrita. A
   outra referência (`AgentInsightsTab.tsx:150`) está **exata**. Convenção 22: a
   referência não nasceu errada, envelheceu — e o **fato** que ela sustenta
   (`useState` com `DEFAULT_INSIGHTS_PERIOD` nas duas telas) continua verdadeiro,
   conferido nas duas.
3. **O enunciado diz que a aba "guarda o período em `useState`" e a página também,
   e isso basta — mas não diz que o ESCRITOR da aba apaga a busca inteira.** Ver
   **D5**: é o achado da varredura, e é o único defeito latente que esta change
   encontra em vez de receber.

### O que a verificação acrescentou ao escopo

**`AgentDetailPage.tsx:76` apaga a busca inteira a cada troca de aba.** Com o
período no endereço, ir para outra aba e voltar **perderia a janela em silêncio** —
o mesmo modo de falha que esta change existe para fechar, reintroduzido por um
caminho diferente. Não é cosmético: é pré-requisito. Entrou no escopo pela
varredura, não pelo enunciado, e é por isso que `agent-catalog-ui` está entre as
capacidades modificadas.

## Goals / Non-Goals

**Goals:**

- o período **viaja** do ranking do sistema até a aba do agente, e o guarda disso
  **navega** — reprovando contra o `HEAD`, onde a aba volta ao padrão;
- o período **sobrevive a recarregamento** e a link colado, nas duas telas;
- endereço **sem** período continua abrindo no padrão, que é o caminho de todo link
  já compartilhado;
- endereço com período **inválido** não quebra a página, e o que ela faz está
  decidido e guardado;
- a troca de aba **deixa de apagar** o resto do endereço.

**Non-Goals:**

- **a #92 não é corrigida** — a coluna Falhas contra o KPI é decisão de vocabulário
  e pede o artboard;
- a **#94** e a nota por grupo da **#90** ficam de fora;
- **nenhum** arquivo de `apps/api`, `apps/workers`, `apps/inbox` nem o
  `nginx.conf` é tocado; nenhuma rota de backend muda;
- **escolha de limites arbitrários** (um calendário, um intervalo digitado) não
  entra: ela mudaria o que o endereço significa, e a **D2** explica por quê;
- `KnowledgeBaseDetailPage.tsx:98`, que tem a **mesma forma** do defeito da **D5**,
  **não é corrigido aqui** — ver *Risks*, onde ele está registrado com gatilho.

## Decisions

### D1 — O período no endereço, e não em estado compartilhado

**Decisão:** parâmetro de consulta, lido e escrito por `useSearchParams` nas duas
superfícies.

**As três formas que estavam na mesa, e por que as outras duas perdem:**

| forma | passa no link? | sobrevive a refresh? | custo |
|---|---|---|---|
| **o período na URL** | **sim** | **sim** | o endereço cresce, e o formato dos limites tem de ser decidido (**D2**) |
| estado compartilhado (contexto, store) | sim | **não** | não polui a URL, e perde **metade** do que a passagem precisa |
| o período só no link do ranking | sim | **não** | resolve a passagem, não resolve o refresh |

**O que decide é que a URL é o único dos três que resolve as duas metades** — e a
casa já usa o mecanismo nesta mesma página, para a aba. Estado compartilhado
resolveria a travessia e deixaria o operador que recarrega a aba de volta no
padrão, que é o mesmo defeito com outro gatilho.

**E há um argumento estrutural de reuso:** `parseTab` já estabeleceu o contrato de
leitura de parâmetro desta página. O período entra como **segunda chave do mesmo
endereço**, com o mesmo vocabulário — não como um segundo mecanismo de estado
concorrendo com o primeiro.

### D2 — O endereço carrega o NOME da janela (`?period=90d`), não os seus limites

**Decisão:** `?period=7d|30d|90d`. Nunca `?from=…&to=…`.

**As duas formas são defensáveis e significam coisas diferentes, e esta é a parte
que precisa do motivo escrito:**

- **instantes absolutos CONGELAM a janela.** Um link compartilhado amanhã responde
  sobre o mesmo intervalo de ontem. É a pergunta *"o que aconteceu naqueles 90
  dias"*;
- **o nome da janela DESLIZA.** Compartilhado amanhã, responde sobre os últimos 90
  dias de amanhã. É a pergunta *"como está indo"*.

**Três razões escolhem a segunda, e a primeira delas é dirimente:**

1. **A janela rolante é requisito vivo, não preferência.** `system-insights-ui`
   exige janela *"rolante em instantes — N × 24 h terminando no instante da
   consulta"*, e tem o cenário *"Nova tentativa consulta a janela atualizada"*. Um
   endereço com instantes absolutos **congelaria** a janela e contradiria os dois:
   a nova tentativa passaria a reconsultar o intervalo velho. Escolher instantes
   aqui exigiria mudar aquele requisito, o que é outra change e outra decisão.
2. **A interface não tem como produzir nem como ler de volta um limite arbitrário.**
   O seletor é um `SegmentedControl` de três opções (`PeriodPicker.tsx:22`). Um
   endereço com instantes só poderia nascer de um retrato de um preset, e a volta —
   instantes → segmento marcado — **não fecha o círculo**: `?from=…&to=…` com 43
   dias não corresponde a nenhum dos três botões, e a tela teria de decidir qual
   marcar ou não marcar nenhum. Isso é interface que não existe, inventada para
   servir um formato de endereço.
3. **O preset é exatamente o que a chave de cache já carrega.** `useSystemInsights.ts:20`
   e `useAgentInsights.ts:33` põem o **nome** do período na chave, de propósito e com
   a razão escrita: janela na chave gera chave nova a cada render e prende a tela em
   carregamento. Ler o preset do endereço alimenta aquele mesmo eixo **sem tocar na
   chave** — a arquitetura de cache não muda uma linha. Instantes no endereço
   obrigariam a decidir, de novo, o que entra na chave.

**O que a escolha custa, declarado:** um link colado hoje e aberto na semana que vem
mostra outros números. **Isso é o que a tela já significa** — o seletor diz "90d", não
"01/07 a 29/09" —, e a página ecoa a janela que a **resposta** devolve
(`SystemInsightsPage.tsx:180-188`, `AgentInsightsTab.tsx:172-180`), então o
intervalo concreto está sempre na tela de quem abre. O link responde *"como está
indo"*, e a tela diz sobre qual intervalo.

**Alternativa considerada e recusada: as duas, com precedência.** Aceitar `period` e,
se ele faltar, `from`/`to`. Recusada por ser dois vocabulários para a mesma pergunta
sem um segundo consumidor que os justifique (convenção 2) — e porque o caso que
pediria instantes (*"me mostre aqueles 90 dias"*) **não tem interface para ser
produzido**, então a precedência serviria um caminho que ninguém percorre.

### D3 — O nome do parâmetro é `period`, e ele convive com `tab`

`/insights?period=90d` e `/agents/<id>?tab=insights&period=90d`.

**Por que não `janela` nem `p`:** os parâmetros desta base são em inglês (`tab`), e
o nome curto economiza caracteres num endereço que ninguém digita à mão, ao custo
de um nome que ninguém lê.

**Por que o valor é o próprio `InsightsPeriod` (`7d`/`30d`/`90d`) e não um número de
dias:** o tipo é `(typeof INSIGHTS_PERIODS)[number]` (`insightsWindow.ts:20`), então
o valor do endereço e o valor do domínio são **a mesma cadeia** — não há mapa a
manter nem segunda fonte de verdade sobre quais janelas existem. `?period=30` exigiria
uma tradução nos dois sentidos para não ganhar nada.

### D4 — Na página do agente, a PÁGINA é dona do endereço; a aba recebe o período como prop

**Decisão:** `AgentDetailPage` lê `period` do endereço e passa `period` e
`onPeriodChange` para `AgentInsightsTab`, **as duas obrigatórias**. A aba perde o
`useState` e não importa `useSearchParams`.

**A alternativa era a aba ler `useSearchParams` ela mesma**, pela simetria com
`SystemInsightsPage` — que é uma *página* e lê o seu próprio endereço. Três razões
escolhem a prop:

1. **Um dono por endereço.** `parseTab` já mora na página. Com a aba lendo o mesmo
   `searchParams`, duas peças passariam a escrever na mesma busca, e o escritor da
   **D5** teria de conhecer uma chave que não é dele para não a apagar. Com a prop,
   o endereço tem **um** leitor e **um** escritor, e a preservação da **D5** deixa de
   depender de a página saber que `period` existe — ela mexe só em `tab`.
2. **Convenção 7.** A página busca e repassa; o componente recebe. `AgentInsightsTab`
   não é apresentacional puro — já hospeda a própria consulta, com a razão escrita em
   `useAgentInsights.ts:22-28` — mas quem o router monta é a página, e o endereço é
   dela.
3. **Prop obrigatória move a fiação para dentro do compilador** (convenção 25). Tirar
   `period` da passagem deixa de compilar; não há sítio futuro que precise *lembrar*
   de passá-lo. É a mesma propriedade que a #83 mediu ao remover a variante morta.

**O custo, declarado:** `AgentInsightsTab.test.tsx` passa a informar duas props no
helper `renderTab` (`:52-69`). É um arranjo mais barato que o alternativo, que
exigiria embrulhar aquele `Wrapper` — hoje sem router nenhum — num `MemoryRouter`.

### D5 — O escritor da aba mexe SÓ na chave que lhe pertence

**O defeito latente, lido no código:** `AgentDetailPage.tsx:76` faz

```ts
setSearchParams(next === OVERVIEW_TAB ? {} : { tab: next });
```

e as duas pernas **substituem a busca inteira**: `{}` esvazia, `{ tab: next }`
descarta tudo que não seja `tab`. Hoje isso não tem sintoma, porque aquele endereço
não tem segundo parâmetro. Com o período lá, trocar de aba e voltar o perde — e o
sintoma seria **exatamente** o desta issue, reintroduzido por outro caminho.

**Decisão:** o escritor passa a copiar a busca atual e a alterar apenas `tab` —
`set` quando a aba não é a visão geral, `delete` quando é.

**Verificado no pacote instalado, não suposto** (convenção 6): `react-router@8.3.0`
declara

```ts
type SetURLSearchParams = (
  nextInit?: URLSearchParamsInit | ((prev: URLSearchParams) => URLSearchParamsInit),
  navigateOpts?: NavigateOptions,
) => void;
```

(`node_modules/react-router/dist/development/lib/dom/lib.d.ts:1567`) — a forma de
atualizador funcional existe nesta versão. **O `prev` é copiado, nunca mutado**: ele
é a instância que o router entrega, e escrever nela seria mexer em estado que não é
nosso.

**O que NÃO muda:** a visão geral continua sendo *ausência* de `tab`, não
`tab=visao-geral`. O contrato de `parseTab` fica intacto; o que muda é só o que o
escritor preserva ao redor dele.

### D6 — Ausência e valor desconhecido caem no padrão, sem reescrever o endereço

**Decisão:** o mesmo contrato de `parseTab`, pelo mesmo motivo escrito lá — reescrever
o endereço só poluiria o histórico de navegação.

**A pergunta que o enunciado deixa aberta é "padrão silencioso ou erro declarado", e a
resposta é silencioso, por simetria medida:** a página já trata identificação de aba
desconhecida assim, e tem cenário de spec para isso
(`agent-catalog-ui`, *"Identificação de aba desconhecida abre a visão geral"*). Duas
chaves do mesmo endereço com disciplinas opostas — uma tolerante, uma que grita —
seriam duas regras para o operador aprender sobre a mesma barra de endereços.

**E há um argumento de proveniência (convenção 13):** um período inválido não é dado do
sistema, é um endereço malformado. A tela não tem o que afirmar sobre ele, e abrir no
padrão não afirma nada de falso: a janela consultada vai para a tela ecoada pela
resposta, então **o operador vê em qual período ele está**, qualquer que tenha sido o
endereço. O silêncio não esconde nada porque a tela já diz a verdade ao lado.

**Guardado nas duas telas**, e o cenário afirma as duas coisas: o padrão **e** que o
endereço não foi reescrito.

### D7 — A leitura mora em `insightsWindow.ts`, e não em módulo novo

**Decisão:** uma função pura exportada, `parsePeriod(value: string | null): InsightsPeriod`,
em `src/features/insights/utils/insightsWindow.ts`.

**Por que ali:** aquele módulo já é o dono do vocabulário — `INSIGHTS_PERIODS`,
`InsightsPeriod`, `DEFAULT_INSIGHTS_PERIOD`, `PERIOD_DAYS`, `PERIOD_LABELS`. A leitura
do parâmetro é a quinta pergunta sobre o mesmo conjunto, e o teste dele já existe
(`insightsWindow.test.ts`).

**Por que função pura exportada e não uma expressão embutida em cada tela:** são **dois**
consumidores reais nesta change (as duas superfícies) mais o escritor do link, e a
alternativa seria a mesma lista de válidos escrita em dois lugares — segunda fonte de
verdade sobre quais janelas existem. É a forma que a convenção 20 já fixou para a
condição de polling, pelos mesmos dois motivos: testável sem montar tela, e perguntada
em mais de um lugar.

**Por que NÃO um módulo novo:** nenhum consumidor fora de `insights` pergunta isso, e um
arquivo a mais para três linhas seria abstração sem consumidor (convenção 2).

### D8 — O relógio não ganha abstração, e os guardas afirmam a LARGURA da janela

**O problema real:** se a janela é relativa, o "agora" que a resolve está no cliente, e
guarda sobre janela relativa com relógio livre é guarda que muda de resultado com o dia.

**O estado medido:** não há `TimeProvider` (ver *Context*, achado 1). O "agora" é
`new Date()` dentro do `queryFn`, e **isso é decisão registrada** em
`insightsWindow.ts:12-14` e nos dois hooks — é o que faz a nova tentativa consultar a
janela atualizada.

**Decisão: não introduzir abstração de relógio, e escrever os guardas sobre a
LARGURA.** O que esta change precisa provar é *"a aba abriu na janela de 90 dias, não na
de 30"*, e isso se afirma sobre a chamada real:

```
(Date.parse(to) - Date.parse(from)) / DAY_MS === 90
```

que é **determinístico com relógio livre** — a diferença entre os dois limites não
depende de quando o teste roda. Fixar o relógio seria necessário para afirmar os
*instantes*, e nenhum cenário desta delta afirma instantes.

**Por que isso importa além da economia:** é consequência direta da **D2**. Com
instantes absolutos no endereço, a tela teria de produzi-los a partir do "agora" **no
render**, e aí o guarda precisaria de relógio controlado para ser reprodutível — e o
"agora" do render é justamente o que `insightsWindow.ts:12` recusa. **A escolha do
preset é o que mantém o relógio fora dos guardas.**

**Declarado para quem vier depois:** se alguma asserção precisar de instante exato,
`vi.setSystemTime` é o duplo da casa — precedente em `useSessions.test.ts:129` e
`InventoryPage.test.tsx:525` —, e a feature `insights` hoje **não o usa em teste
nenhum**. Introduzi-lo é acréscimo de arranjo, não reuso.

### D9 — Nenhum `Record` novo de exaustividade, e a razão é que a união já é derivada

A convenção 25 manda, para declaração de tipo, amarrar a união a um `Record` exaustivo
no teste — e **avisa** que uma lista paralela (`readonly X[]`) **não** é exaustividade,
porque um membro novo na união compila com o array intacto.

**Aqui a armadilha não existe, e é por construção:**

```ts
export const INSIGHTS_PERIODS = ['7d', '30d', '90d'] as const;
export type InsightsPeriod = (typeof INSIGHTS_PERIODS)[number];   // :20
const PERIOD_DAYS: Record<InsightsPeriod, number> = { … };        // :26
export const PERIOD_LABELS: Record<InsightsPeriod, string> = { … };// :36
```

A união é **derivada da lista**, não declarada ao lado dela — então não há como
acrescentar membro à união sem tocar a lista, e `PERIOD_DAYS`/`PERIOD_LABELS` já são
`Record<InsightsPeriod, …>`: quem acrescentar `'180d'` ao array **não compila** até
classificar o novo membro nos dois. O caso que a #89 encontrou era o oposto — união
escrita à mão com um array paralelo.

**Decisão: nenhum `Record` novo.** `parsePeriod` valida por pertencimento à própria
lista (`INSIGHTS_PERIODS.includes`), então um membro novo passa a ser aceito no endereço
**sem nenhuma edição** — o que é o comportamento certo, e não um esquecimento a guardar.

**Registrado como recusa, com o número que a sustenta**, porque é exatamente o tipo de
guarda que uma revisão pediria por reflexo depois da #83/#89.

### D10 — O guarda da travessia mora em `router.test.tsx`, ao lado do que já existe

**A régua:** *guarda que afirma o meio do caminho não prova o fim dele* — e **foi esta
tela que a ensinou**. `router.test.tsx:245-258` já traz o registro por escrito: existem
guardas nas duas pontas (o `href` em `AgentConsumptionCard.test.tsx:116`, a abertura da
aba em `AgentDetailPage.test.tsx:600`) e **nenhum dos dois navega**.

**Decisão:** o guarda da janela que atravessa é um `it()` próprio em `router.test.tsx`,
**ao lado** do caso de 26/09 que atravessa a aba — e não uma asserção acrescentada
dentro dele. Convenção 18: *numa tela cujo valor está no que ela afirma, cada afirmação
é um `it()` próprio, senão ela some na primeira refatoração que "limpar" o teste.*

O caso escolhe um período **diferente do padrão** na página do sistema, aciona o nome do
agente, e afirma três coisas: a aba ativa, o período marcado no seletor, e que
`getAgentInsights` foi chamado com janela de **90 dias**. A terceira é a que reprova
contra o `HEAD`, onde a aba abre em 30.

**E um par negativo**, porque é o modo de falha silencioso desta issue: **nenhuma**
chamada à rota do agente com janela de 30 dias.

## A varredura de escopo — universo ANTES do filtro

Convenção 22, sétima ocorrência: *para toda varredura cujo número vai ser citado,
enumerar o universo antes de filtrar, e conferir o critério contra um mais largo.*
Varrido **pela forma**, nunca pelo número da issue — *varredura por número de issue acha
o que fala do assunto, não o que o executa.*

| # | critério | comando | resultado |
|---|---|---|---|
| A | **largo** — todo `useSearchParams` | `grep -rln "useSearchParams" src/` | **2** arquivos |
| B | estreito — `setSearchParams` | `grep -rn "setSearchParams" src/` | **2** arquivos, 4 linhas |
| C | o default do período | `grep -rln "DEFAULT_INSIGHTS_PERIOD" src/` | **5** arquivos |
| D | **largo** — o tipo inteiro | `grep -rln "InsightsPeriod" src/` | **10** arquivos, dos quais **8** reais |
| E | a passagem | `grep -rln "tab=insights" src/` | **4** arquivos / 8 linhas, e **1** só de produção |
| F | quem monta os dois componentes | `grep -rln "<AgentInsightsTab\|<AgentConsumptionCard"` | **2 + 2**, todos já na lista |
| G | **3ª dimensão de blast radius** — quem mocka o módulo de API | `grep -rln "vi.mock(.*insightsApi\|vi.mock(.*useSystemInsights\|vi.mock(.*useAgentInsights"` | **6** arquivos |

**O que a comparação largo × estreito acusou, e que a leitura de um critério só não
acusaria:**

- **A × B:** os dois critérios devolvem os **mesmos 2 arquivos**, e o largo confirma que
  não há terceiro consumidor de endereço no painel. Sem o largo, "2 arquivos" seria
  amostra.
- **C × D:** o default aparece em 5 arquivos, o **tipo** em 10. Os 5 a mais são os hooks e
  os testes que tipam o período sem semeá-lo — **nenhum** deles precisa mudar, porque
  recebem o período de quem já o resolveu. É resultado **negativo** e vale registrado:
  varrer pelo default sozinho teria dado a lista certa por acaso.
- **E o critério largo também ERRA PARA CIMA, que é a direção que a sétima ocorrência da
  convenção 22 não registrava.** Dos 10 de D, **dois são falso positivo**:
  `insightsApi.ts:69` e `insightsApi.test.ts:32` casam `InsightsPeriod` porque citam
  **`InsightsPeriod.MissingBoundMessage`**, que é o nome de um tipo do `apps/api` dentro de
  um comentário. São 8 consumidores reais do tipo TypeScript. A régua escrita até aqui é
  *"o critério estreito esconde um buraco"*; esta medição acrescenta que **o largo cobra
  uma conferência item a item**, e que o número citado é o do critério **conferido**, não o
  do mais largo.
- **E:** a varredura pela **marca** (`tab=insights`) devolve 4 arquivos, 8 linhas, e **um
  único** de produção. É a régua da #83 — *varredura por nome perde quem toca só a marca* —
  e aqui ela fecha nos dois sentidos: existe **um** sítio de link, não dois.
- **G é a dimensão que nenhuma leitura do código de produção aponta:** `router.test.tsx`
  mocka `insightsApi` e é de **outra feature**. Foi assim que a quinta medição da
  convenção 18 descobriu um arquivo a mais, e aqui ele já está na lista por ser onde o
  guarda da travessia mora (**D10**).

**A lista fechada de arquivos que esta change toca — 11, todos modificados, nenhum
criado:**

```
produção (5)
  src/features/insights/utils/insightsWindow.ts
  src/features/insights/pages/SystemInsightsPage.tsx
  src/features/insights/components/AgentInsightsTab.tsx
  src/features/insights/components/AgentConsumptionCard.tsx
  src/features/agents/pages/AgentDetailPage.tsx

teste (6)
  src/features/insights/utils/insightsWindow.test.ts
  src/features/insights/pages/SystemInsightsPage.test.tsx
  src/features/insights/components/AgentInsightsTab.test.tsx
  src/features/insights/components/AgentConsumptionCard.test.tsx
  src/features/agents/pages/AgentDetailPage.test.tsx
  src/app/router.test.tsx
```

**`PeriodPicker.tsx` e `PeriodPicker.test.tsx` NÃO entram, e é conferido:** o seletor
recebe `value`/`onChange` (`PeriodPicker.tsx:15-18`) e não sabe de onde o valor vem. A
mudança é de **origem** do valor, não de interface do seletor.

**Sem blast radius de fixture.** Nenhum campo obrigatório é acrescentado a tipo de
domínio compartilhado, então a dimensão que arrastou 21 arquivos na quarta medição da
convenção 18 **não existe aqui**. Os dois tipos que ganham campo obrigatório são
`AgentInsightsTabProps` e `AgentConsumptionCardProps`, com **2 consumidores cada**,
enumerados pelo `tsc` (varredura F).

## Convenção 18 — vigésima sexta medição (projeção)

### A baseline, remedida em árvore limpa — não herdada

**`apps/frontend`: 1413 testes / 118 arquivos, zero falhas, 131,22 s.**

Convenção 22 — o estado vai na mesma frase do número: medido em **`515883d`** (a `main`
com a #83/#89, conferida por SHA e por `git merge-base --is-ancestor`), **árvore limpa**
(`git status --porcelain` vazio), `load average` **4,42 em 12 núcleos** na largada, VM do
Podman a 23,6% de CPU, dois contêineres de desenvolvimento no `podman ps`, **nada na
porta 9222**, **zero** `Unhandled Error` de pool.

**Gatilho de recalibração:** remedir ao criar arquivo de teste novo em `apps/frontend`,
ou antes de qualquer fechamento em commit diferente de `515883d`.

**Saída completa guardada, não filtrada:**
`~/.cache/buteco-agents/issue-85/baseline-515883d-frontend.txt` — fora do diretório de
sessão, e sem passar por `grep` nem `tail` (convenção 19, terceira forma).

**O regime foi conferido ANTES de lançar, não depois** (convenção 19, quarta forma): a
quarta forma diz que `Unhandled Error` de pool é sintoma de ambiente, e que a VM carregada
já produziu 21× de duração e 14 falsos defeitos. `load` 4,42 contra o teto de ~12
(1 × núcleos) autorizou a medição; o número bate com o último registrado para este commit,
o que é a confirmação independente de que o regime estava limpo.

### Casos — por categoria, derivados dos cenários da delta

A régua da 5a-4: *projetar negativas lendo os cenários da delta, não a lista de coisas que
a tela se recusa a dizer* — e contar **estados observáveis**, não afirmações. A delta tem
**13 cenários novos** (6 em `system-insights-ui`, 5 em `agent-insights-ui`, 2 em
`agent-catalog-ui`), e cada um é um `it()`.

| categoria | projetado | onde |
|---|---|---|
| **novos** | **16** | 13 dos cenários da delta + 3 de `parsePeriod` (válido / ausente / desconhecido) |
| — dos quais **de asserção** | **8** | `href` do link (2), `parsePeriod` (3), padrão sem parâmetro (2), prop da aba (1) |
| — dos quais **com arranjo próprio** | **8** | endereço de abertura (2), escrita no endereço (2), endereço não reescrito (2), preservação na troca de aba (2), travessia (1) — *sobrepostos nos arquivos* |
| **negativas novas** | **3** | o link não carrega o padrão; nenhuma consulta com 30 dias na travessia; o endereço não é reescrito no caso inválido |
| **reforçados** | **2** | os dois guardas de `href` de `AgentConsumptionCard.test.tsx:105,120` ganham a cláusula do período |
| **adaptados** | **0 casos** | mas **3 helpers de arranjo**: `renderTab` (+2 props), `renderCard` (+1 prop), o wrapper de `SystemInsightsPage.test.tsx` (+ endereço inicial) |
| **removidos** | **0** | nenhuma declaração sai; nenhum comportamento deixa de existir |
| **de fiação** | **0 acrescentados** | ver a declaração falsificável abaixo |
| **saldo de casos** | **+16** → **1429** | sobre 1413 |
| **arquivos de teste** | **118** → **118** | nenhum criado |
| **arquivos tocados** | **11 modificados, 0 criados** | lista fechada acima |

**A contagem "com arranjo" soma mais que a de arquivos porque dois cenários dividem o
mesmo arranjo** — na mesma `describe`, o endereço inicial é montado uma vez. Projetado com
o custo unitário cheio ainda assim, porque a quinta medição errou **para baixo** ao contar
arranjo compartilhado como grátis.

### A régua da fiação — projetada com declaração falsificável

Com os guardas verdes, tirar a prop ou o parâmetro de um consumidor e **ler a
distribuição, não a contagem**: reprovar fora do arquivo do próprio consumidor é o sinal.
Três fiações, e as três têm resposta prevista:

| fiação | o que tirar | quem deve reprovar | é sinal de fiação? |
|---|---|---|---|
| o link carrega o período | `period` do `to=` em `AgentConsumptionCard.tsx` | o teste dele **e** `router.test.tsx` | **sim** — reprova fora do próprio arquivo |
| a página repassa o período | a prop na montagem de `<AgentInsightsTab>` | o **`tsc`** | não precisa de guarda (**D4**) |
| a troca de aba preserva o resto | a cópia da busca em `handleTabChange` | `AgentDetailPage.test.tsx` **e** `router.test.tsx` | **sim**, se a travessia passar por troca de aba |

**Declaração falsificável: nenhum guarda extra será preciso.** A camada de fiação entre o
ranking e a aba **já tem** quem a cubra de fora — o guarda da travessia da **D10**, que
existe por outra razão. **Se a régua obrigar um guarda novo, esta previsão está errada**, e
o motivo a registrar é qual costura ficou sem cobertura externa. Precedente dos dois sinais:
a #84/#86 obrigou um nono guarda; a #83 não tinha fiação e nenhum foi preciso.

### Linhas — e aqui a projeção aplica as DUAS regras que a vigésima quinta deixou abertas

Critério de contagem, para poder ser recontado: **linhas adicionadas não vazias** de
`git diff -U0`; comentário = linha que começa com `//`, `*` ou `/*`.

**Regra 1 — o comentário é o produto, e se projeta dos DOIS lados.** A vigésima quinta
aplicou-a só à produção e errou o teste em **+43%**, com 69 das 114 linhas de teste sendo
comentário (1,53:1).

**Regra 2 — guarda com arranjo próprio custa o dobro do de asserção.** Medido lá: 17 e 23
linhas de lógica, não um `expect`.

**Produção — a contagem dos registros de mecanismo, que é o que projeta a mistura:**

| # | registro de mecanismo entregue | onde |
|---|---|---|
| 1 | preset × instantes, e qual pergunta o link responde (**D2**) | `insightsWindow.ts` |
| 2 | ausência canônica e desconhecido silencioso, e por que não grita (**D6**) | `insightsWindow.ts` |
| 3 | o escritor mexe só na chave que lhe pertence, e o que `{}` apagava (**D5**) | `AgentDetailPage.tsx` |
| 4 | por que a página é dona do endereço e a aba recebe prop (**D4**) | `AgentDetailPage.tsx` / `AgentInsightsTab.tsx` |
| 5 | por que o link escreve o parâmetro mesmo com as duas telas o lendo (endereço diferente) | `AgentConsumptionCard.tsx` |

**Cinco registros**, e **zero registros existentes tornados falsos** — a parcela que
dominou a vigésima quarta. Dois blocos ganham **cláusula** sem deixar de ser verdade
(`AgentConsumptionCard.tsx:237`, `AgentDetailPage.tsx:34`); nenhum é corrigido com a causa.
**Declarar isso é como esta projeção evita o erro da vigésima quarta**, que leu um erro de
volume como erro de régua.

| | projetado |
|---|---|
| criados | **0 arquivos / 0 linhas** |
| **produção — lógica** | **~24** |
| **produção — comentário** | **~84** |
| **produção — total** | **~108**, com mistura **~3,5 : 1** |
| **teste — 8 casos de asserção** | ~20 linhas cada → **~160** |
| **teste — 8 casos com arranjo** | ~38 linhas cada → **~304** |
| **teste — total** | **~464**, dos quais **~278 comentário** (1,5:1) |
| **total adicionado (não vazio)** | **~572** |
| **removidas** | **~10** (dois `useState`, a linha do escritor, imports que saem) |

**A hipótese sobre a MISTURA, declarada falsificável antes de medir:** cinco registros de
mecanismo sobre uma base pequena de lógica dão **~3,5:1 na produção**. **Falsificação:
abaixo de 2,0:1 a contagem de registros está errada** — a vigésima terceira, uma change de
consumo + correção com menos registros, mediu **1,98:1**, então cinco registros têm de
bater isso. E **acima de 5,0:1** significaria que estou em território de remoção (a
vigésima quinta), que esta change não é.

**A hipótese sobre o ERRO, também falsificável:** o desvio vai estar **no teste**, como nas
duas últimas, mas **menor que +43%**, porque as duas causas daquele erro foram corrigidas
nesta projeção (comentário projetado no teste; arranjo separado de asserção no custo
unitário). **Se o erro de teste for novamente ≥ +40%, as duas correções não bastam** e o
custo unitário em si está errado — e aí o que se recalibra é o número, não o método.

**A projeção não será corrigida depois de medir.** Corrigi-la apagaria a medição.

## Risks / Trade-offs

Convenção 10 — todo risco nomeado tem contraparte verificável, ou a justificativa
explícita de por que não tem.

- **[O link compartilhado desliza: aberto amanhã, mostra outro período]** → **não é
  mitigável, é a decisão da D2**, e a contraparte é informativa, não defensiva: a tela ecoa
  a janela que a **resposta** devolve, então quem abre vê o intervalo concreto. Coberto pelo
  cenário vivo *"A janela vem da resposta"* (`agent-insights-ui`), que não muda nesta change.
- **[A troca de aba apaga o período]** → é o defeito latente da **D5**, e tem dois cenários
  novos em `agent-catalog-ui` (*trocar de aba preserva*, *voltar à visão geral remove só a
  aba*) mais o guarda de fiação da tabela acima.
- **[O guarda da travessia passar verde com a travessia quebrada]** → é a régua que **esta
  tela ensinou**, e a contraparte é a **D10**: o caso **navega** por `userEvent.click`, e
  reprova contra o `HEAD`. **Verificação obrigatória na implementação:** reintroduzir o
  defeito (tirar `period` do `to=`) e **ver reprovar** antes de manter a correção
  (convenção 15), conferindo que ele reprova **no componente que a correção toca** — a
  segunda forma da convenção 15.
- **[O guarda do "endereço não reescrito" passar por construção]** → é a quarta forma da
  convenção 15: escrever o guarda no mesmo passo em que se tira a reescrita o faria passar
  sozinho. **Contraparte:** o guarda é escrito **antes**, contra uma reescrita deliberada, e
  só então ela é retirada. A precondição afirmada é o **estado observável mais próximo do
  elemento negado** — o endereço **imediatamente após** a montagem, não depois de uma
  interação que já o teria normalizado.
- **[`KnowledgeBaseDetailPage.tsx:98` tem a MESMA forma do defeito da D5 e não é corrigido]**
  → **não é defeito hoje**: aquele endereço tem uma chave só (`tab`), então substituir a busca
  inteira não apaga nada. **É armadilha latente da família da convenção 25** — a declaração que
  não custa nada a ninguém até alguém pôr um segundo parâmetro ali. **Gatilho observável: o
  primeiro segundo parâmetro no endereço do detalhe de base de conhecimento.** Sem contraparte
  de teste **por decisão**: guardar o comportamento correto de um parâmetro que não existe
  exigiria inventar o parâmetro. **Candidato a issue própria**, e a abertura é do dono —
  convenção 23 manda virar issue na hora, e este `design.md` é o registro até lá.
- **[A medição da convenção 18 sair inválida por regime carregado]** → `load average` e
  `podman ps` escritos **na mesma frase** do número, e **abortar** acima de ~12. Contraparte:
  é tarefa escrita no `tasks.md`, com a saída completa guardada em arquivo, não disciplina de
  quem roda.
- **[A conferência visual não acontecer]** → esta change **muda o endereço, não o desenho**:
  nenhum token, nenhuma cor, nenhuma composição. **Mas a convenção 14 ainda morde**, porque o
  que muda é o **estado em que a tela abre**, e isso é visível: o seletor marcado em 90d na
  aba que abriu pelo ranking. Contraparte: tarefa própria de conferência manual, nos dois
  esquemas, com o navegador **encerrado por porta** (`lsof -ti :9222 | xargs kill`) antes de
  cada execução de suíte.

## Open Questions

Nenhuma de negócio ou de produto. As três perguntas que o enunciado abriu foram
**decididas com o motivo escrito**, não deixadas abertas: a forma (**D1**), o que o endereço
carrega (**D2**) e o que fazer com valor inválido (**D6**). A quarta — o `TimeProvider` —
era pergunta sobre o estado do repositório, e a resposta está no *Context*: ele não existe, e
a **D8** diz o que isso muda.

**Uma decisão pertence ao dono e não a esta change:** abrir issue própria para
`KnowledgeBaseDetailPage.tsx:98` (ver *Risks*). Registrada aqui porque o que não tem issue
não existe depois do archive.
