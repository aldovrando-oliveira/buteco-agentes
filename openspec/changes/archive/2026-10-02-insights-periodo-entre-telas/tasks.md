# Tarefas — o período entre as duas telas de Insights (#85)

**Todas as tarefas rodam em `apps/frontend`.** Nenhuma toca `apps/api`,
`apps/workers`, `apps/inbox` nem `deploy/`. As tarefas de documentação (grupo 8)
são as únicas fora de `apps/frontend`, na raiz do repositório.

**Branch:** `feat/85-insights-periodo-entre-telas`, criada de
`515883da0fe693b8ff9204c25bc39bf1b8a26a1a` — a `main` com a #83/#89, conferida
**por SHA** e não pela mensagem do `git status` (convenção 24).

---

## 1. A conferência de escopo — a lista fechada, varrida PELA FORMA

- [x] 1.1 **Conferir a branch por SHA**, não pela mensagem (`apps/frontend`, raiz).
      Feito: `git rev-parse main` = `origin/main` = `515883da0fe6…`;
      `git merge-base --is-ancestor bfb114b HEAD` confirma que a #83/#89 está em
      `HEAD`; `git status --porcelain` vazio.

- [x] 1.2 **Varrer o escopo pela FORMA, enumerando o universo ANTES de filtrar**
      (convenção 22, sétima ocorrência). Saída colada abaixo, não parafraseada.

```
--- A: grep -rln "useSearchParams" src/
src/features/agents/pages/AgentDetailPage.tsx
src/features/knowledge-bases/pages/KnowledgeBaseDetailPage.tsx

--- C: grep -rln "DEFAULT_INSIGHTS_PERIOD" src/
src/features/insights/components/AgentInsightsTab.tsx
src/features/insights/components/PeriodPicker.test.tsx
src/features/insights/pages/SystemInsightsPage.tsx
src/features/insights/utils/insightsWindow.test.ts
src/features/insights/utils/insightsWindow.ts

--- E: grep -rln "tab=insights" src/
src/app/router.test.tsx
src/features/agents/pages/AgentDetailPage.test.tsx
src/features/insights/components/AgentConsumptionCard.test.tsx
src/features/insights/components/AgentConsumptionCard.tsx

--- G: quem mocka o módulo de API (3ª dimensão de blast radius, convenção 18)
src/app/router.test.tsx
src/features/agents/pages/AgentDetailPage.test.tsx
src/features/insights/api/useAgentInsights.test.tsx
src/features/insights/api/useSystemInsights.test.tsx
src/features/insights/components/AgentInsightsTab.test.tsx
src/features/insights/pages/SystemInsightsPage.test.tsx
```

      **Contagens conferidas uma a uma, não só pelo `wc -l`:** A=2, B=2 arquivos /
      4 linhas, C=5, D=10 **dos quais 8 reais** (`insightsApi.ts:69` e
      `insightsApi.test.ts:32` casam só por citarem
      `InsightsPeriod.MissingBoundMessage`, um tipo do `apps/api`, em comentário),
      E=4 arquivos / 8 linhas / **1 de produção**, G=6.

- [x] 1.3 **A LISTA FECHADA — 11 arquivos, todos modificados, nenhum criado.**

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

      **Fora da lista por conferência, não por omissão:** `PeriodPicker.tsx` e o
      teste dele (o seletor recebe `value`/`onChange` e não sabe de onde o valor
      vem — muda a **origem**, não a interface); os quatro arquivos de hook/API que
      tipam `InsightsPeriod` sem semeá-lo; `KnowledgeBaseDetailPage.tsx` (mesma
      forma do defeito da D5, **não é defeito hoje** — ver 8.5).

- [x] 1.4 **Ao fechar, reconferir a lista contra o `git diff --name-only`** e
      registrar aqui qualquer arquivo que apareceu e não estava projetado, **com a
      causa** — é o que separa "a projeção errou" de "o escopo cresceu"
      (convenção 18: *medição de método só compara o escopo que estava projetado*).

## 2. A baseline — remedida em árvore limpa, não herdada

- [x] 2.1 **Conferir o REGIME antes de lançar**, não depois (convenção 19, quarta
      forma): `uptime`, `ps aux | sort -nrk 3 | head`, `podman ps`, e
      `lsof -ti :9222`. Medido: `load average` **4,42** em **12 núcleos**, VM do
      Podman a 23,6% de CPU, dois contêineres de desenvolvimento no `podman ps`,
      **nada na porta 9222**. Abaixo do teto de ~12 (1 × núcleos) → **autorizado**.

- [x] 2.2 **Medir a baseline** (`apps/frontend`): `npm test` em `515883d`, árvore
      limpa. **1413 testes / 118 arquivos, zero falhas, 131,22 s**, `exit=0`,
      **zero** `Unhandled Error` de pool.

- [x] 2.3 **Guardar a SAÍDA COMPLETA em arquivo, fora do diretório de sessão** —
      não filtrada, não resumida, sem `| grep` nem `| tail` (convenção 19, terceira
      forma: `| grep … | head -N` fecha o cano e mata o produtor por `SIGPIPE`).
      Em `~/.cache/buteco-agents/issue-85/baseline-515883d-frontend.txt`.

- [x] 2.4 **Pendurar o gatilho de recalibração** (convenção 22): remedir ao criar
      arquivo de teste novo em `apps/frontend`, ou antes de qualquer fechamento em
      commit diferente de `515883d`.

## 3. A leitura do parâmetro — a peça que as duas telas dividem

- [x] 3.1 (`apps/frontend`) Em `src/features/insights/utils/insightsWindow.ts`,
      acrescentar `parsePeriod(value: string | null): InsightsPeriod` — função
      **pura exportada**, validando por pertencimento a `INSIGHTS_PERIODS`, com
      ausência e valor desconhecido caindo em `DEFAULT_INSIGHTS_PERIOD` (D6, D7).

- [x] 3.2 (`apps/frontend`) Registrar no mesmo arquivo os dois registros de
      mecanismo que ele passa a carregar: **preset × instantes absolutos, e qual
      pergunta o link responde** (D2), e **por que ausência/desconhecido é
      silencioso em vez de declarado** (D6). O comentário é o produto aqui.

- [x] 3.3 (`apps/frontend`) Em `insightsWindow.test.ts`, os três casos de asserção:
      valor reconhecido, ausência, valor desconhecido.

- [x] 3.4 (`apps/frontend`) **NÃO escrever `Record` novo de exaustividade**, e deixar
      a recusa escrita no teste com o número que a sustenta (D9): a união é
      `(typeof INSIGHTS_PERIODS)[number]`, **derivada** da lista, e `PERIOD_DAYS` /
      `PERIOD_LABELS` já são `Record<InsightsPeriod, …>` — membro novo não compila
      até ser classificado. A armadilha da #89 era união escrita à mão com array
      paralelo, e **não** é esta forma.

## 4. A página do sistema — o período no endereço

- [x] 4.1 (`apps/frontend`) Em `SystemInsightsPage.tsx:146`, trocar
      `useState(DEFAULT_INSIGHTS_PERIOD)` por leitura de `useSearchParams` via
      `parsePeriod`, e a troca de período passa a **escrever** o endereço.
      **Sem reescrever o endereço** quando o parâmetro falta ou é inválido (D6).

- [x] 4.2 (`apps/frontend`) Em `AgentConsumptionCard.tsx`, a prop `period`
      obrigatória, e o `to=` da linha 239 passa a carregar `&period=<atual>`.
      Registrar **por que o link escreve o parâmetro mesmo com as duas telas o
      lendo**: a travessia vai para um endereço diferente, onde o parâmetro não é
      herdado por si.

- [x] 4.3 (`apps/frontend`) Em `SystemInsightsPage.test.tsx`, o wrapper passa a
      aceitar endereço inicial, e os quatro guardas: endereço decide a abertura;
      trocar escreve o endereço; sem parâmetro abre no padrão **sem reescrever**;
      parâmetro inválido abre no padrão **sem reescrever**.

- [x] 4.4 (`apps/frontend`) Em `AgentConsumptionCard.test.tsx`, **reforçar** os dois
      guardas de `href` existentes (`:105` e `:120`) com a cláusula do período, e
      acrescentar a negativa: **o link não carrega o padrão quando o período em
      vigor é outro**.

## 5. A aba do agente — o endereço é da página, o período é prop

- [x] 5.1 (`apps/frontend`) Em `AgentInsightsTab.tsx:150`, remover o `useState` e
      receber `period` e `onPeriodChange` como props **obrigatórias** (D4) — prop
      obrigatória move a fiação para dentro do compilador (convenção 25).

- [x] 5.2 (`apps/frontend`) Em `AgentDetailPage.tsx`, ler o período do endereço com
      `parsePeriod` e repassá-lo, escrevendo o endereço na troca.

- [x] 5.3 (`apps/frontend`) **O escritor da aba passa a mexer só na chave que lhe
      pertence** (D5): `AgentDetailPage.tsx:76` deixa de substituir a busca inteira
      e passa a **copiar** `prev` e a `set`/`delete` apenas `tab`. Usar a forma de
      atualizador funcional, **conferida no pacote instalado** —
      `react-router@8.3.0`, `lib/dom/lib.d.ts:1567`. **Copiar `prev`, nunca mutá-lo**:
      é a instância que o router entrega.

- [x] 5.4 (`apps/frontend`) Registrar em `AgentDetailPage.tsx` os dois registros de
      mecanismo: **o que `{}` e `{ tab: next }` apagavam**, e **por que a página é
      dona do endereço e a aba recebe prop**.

- [x] 5.5 (`apps/frontend`) Em `AgentInsightsTab.test.tsx`, `renderTab` ganha as duas
      props, e o guarda de que a aba consulta a janela do período **recebido** —
      sem `MemoryRouter`, que a D4 evita de propósito.

- [x] 5.6 (`apps/frontend`) Em `AgentDetailPage.test.tsx`, os cinco guardas: endereço
      decide o período da aba; aba sem período abre no padrão; período inválido abre
      no padrão **sem reescrever**; trocar de período escreve o endereço mantendo
      `tab`; **trocar de aba preserva o período** e **voltar à visão geral remove só
      `tab`**.

## 6. A travessia — o guarda que NAVEGA

- [x] 6.1 (`apps/frontend`) Em `src/app/router.test.tsx`, um `it()` **próprio** ao
      lado do caso de 26/09 (`:259`), **nunca uma asserção dentro dele** (D10): escolher
      um período **diferente do padrão** na página do sistema, acionar o nome do
      agente por `userEvent.click`, e afirmar a aba ativa, o período marcado, **e**
      que `getAgentInsights` recebeu janela de **90 dias**.

- [x] 6.2 (`apps/frontend`) O **par negativo**: **nenhuma** chamada à rota do agente
      com janela de 30 dias.

- [x] 6.3 (`apps/frontend`) **Afirmar a LARGURA, não os instantes** (D8):
      `(Date.parse(to) - Date.parse(from)) / DAY_MS === 90`, que é determinístico com
      relógio livre. **Não introduzir abstração de relógio nem `vi.setSystemTime`** —
      a feature `insights` não os usa, e nenhum cenário desta delta afirma instante.

- [x] 6.4 (`apps/frontend`) **O guarda só vale depois de ter falhado contra o defeito
      real** (convenção 15): rodar contra o `HEAD` original — onde a aba abre em 30
      dias — e **ver reprovar**. Depois conferir que ele reprova **no componente que
      a correção toca** (segunda forma da convenção 15), não numa ponta vizinha.

- [x] 6.5 (`apps/frontend`) **Os guardas de "o endereço não é reescrito" escritos
      ANTES de retirar a reescrita** — escrevê-los no mesmo passo os faria passar por
      construção (quarta forma da convenção 15). A precondição afirmada é o **estado
      observável mais próximo do elemento negado**: o endereço **imediatamente após a
      montagem**, não depois de uma interação que já o normalizaria.

## 7. A régua da fiação, a suíte e a medição

- [x] 7.1 (`apps/frontend`) **A régua da fiação, com os guardas verdes**: tirar
      `period` do `to=` de `AgentConsumptionCard.tsx` e **ler a distribuição, não a
      contagem** — reprovar **fora** do arquivo do próprio consumidor
      (`router.test.tsx`) é o sinal de fiação. Repetir tirando a preservação de
      `handleTabChange`.

- [x] 7.2 (`apps/frontend`) **Registrar o resultado da régua contra a previsão
      falsificável do `design.md`**: *nenhum guarda extra será preciso*. Se um for,
      escrever **qual costura ficou sem cobertura externa** — a #84/#86 obrigou um
      nono guarda, a #83 não precisou de nenhum.

- [x] 7.3 (`apps/frontend`) **Conferir o regime de NOVO antes de rodar a suíte de
      fechamento**, não só na baseline: `uptime` + `podman ps`, e **encerrar o
      navegador da conferência por PORTA** (`lsof -ti :9222 | xargs kill`), nunca por
      padrão de linha de comando — `pkill -f` já não casou com um processo lançado de
      dentro do diretório, e a medição seguinte saiu falsa. **Abortar acima de ~12.**

- [x] 7.4 (`apps/frontend`) Rodar a suíte cheia e **guardar a saída completa em
      arquivo**, fora do diretório de sessão, **sem filtrar** — é tarefa escrita aqui
      justamente para não depender de alguém lembrar.

- [x] 7.5 **Medir a convenção 18 — vigésima sexta medição — POR CATEGORIA.** Feito,
      abaixo.

- [x] 7.6 Medir as linhas adicionadas e removidas, separadas, com a mistura
      comentário:lógica **nos dois lados**. Feito, abaixo.

- [x] 7.7 **Veredito das duas hipóteses falsificáveis.** Feito, abaixo.

- [x] 7.8 **A projeção NÃO foi corrigida.** O `design.md` continua com os números
      projetados; a medição está aqui.

### A medição — vigésima sexta

**Suíte de fechamento: `apps/frontend` 1431 / 118, zero falhas, 156,20 s**, contra a
baseline de **1413 / 118** medida em árvore limpa no mesmo commit. Regime: `load
average` **3,88** em 12 núcleos na largada, os dois contêineres de desenvolvimento no
`podman ps`, porta 9222 livre, **zero** `Unhandled Error` de pool. Saída completa em
`~/.cache/buteco-agents/issue-85/fechamento-frontend.txt`.

#### Casos — o saldo erra +12,5%, e as categorias erram em TRÊS lugares

| categoria | projetado | medido | erro |
|---|---|---|---|
| **novos** | 16 | **18** | **+12,5%** |
| negativas novas (por afirmação negada) | 3 | **3** | **exato** |
| **reforçados** | 2 | **1** | **−50%** |
| adaptados — casos | 0 | **0** | exato |
| **adaptados — helpers de arranjo** | **3** | **4** | **+33%** |
| removidos | 0 | **0** | exato |
| **de fiação acrescentados** | **0** | **0** | **exato** |
| **saldo de casos** | +16 → 1429 | **+18 → 1431** | **+12,5%** |
| arquivos de teste | 118 | **118** | **exato** |
| **arquivos tocados** | **11 modificados, 0 criados** | **11 / 0** | **exato** |

Distribuição dos 18, conferida arquivo a arquivo contra o `HEAD`: `router.test.tsx`
+2, `AgentDetailPage.test.tsx` +6, `SystemInsightsPage.test.tsx` +4,
`AgentInsightsTab.test.tsx` +2, `insightsWindow.test.ts` +3,
`AgentConsumptionCard.test.tsx` +1.

**A causa dos +2, e ela ESTENDE a régua da vigésima quinta.** A projeção mapeou os 13
cenários da delta 1:1 para `it()` e somou 3 unitários. Três desvios, em direções
opostas:

- **−1: um cenário da delta virou REFORÇO, não caso novo.** *"O link do ranking carrega
  o período em vigor"* entrou como cláusula no guarda de `href` que já existia, porque a
  afirmação positiva já estava escrita ali e duplicá-la seria dois casos dizendo a mesma
  coisa. **Cenário de spec não implica caso novo** — ele pode já ter dono;
- **+1: uma cláusula `AND` que é NEGATIVA virou `it()` próprio.** O cenário da travessia
  trazia *"AND nenhuma consulta é feita com a janela do período padrão"*. Pela régua de
  que cada negativa é um `it()`, ela saiu do caso positivo;
- **+2: guardas do MECANISMO, que não têm cenário de spec nenhum.** Os dois de
  `AgentInsightsTab.test.tsx` afirmam a **D4** — que a aba consulta o período *recebido*
  e que ela *entrega* a troca a quem possui o endereço. Não é comportamento de tela: é a
  decisão de arquitetura. A vigésima quinta achou esta classe como
  `expect(orfas).toEqual([])` e a nomeou *"nem toda negativa tem cenário de spec"*;
  **aqui ela aparece como guarda de fiação de prop, e não é negativa** — então a régua é
  mais larga do que a 25ª a escreveu: **nem todo guarda tem cenário de spec**, negativa
  ou não.

#### Linhas — e o desvio MUDOU DE LADO

Critério: linhas **adicionadas não vazias** de `git diff -U0`; comentário = linha
iniciada por `//`, `*` ou `/*`.

| | projetado | medido | erro |
|---|---|---|---|
| criados | 0 arquivos / 0 linhas | **0 / 0** | **exato** |
| **produção — lógica** | ~24 | **50** | +108% |
| **produção — comentário** | ~84 | **161** | +92% |
| **produção — total** | ~108 | **211** | **+95%** |
| **produção — mistura** | ~3,5 : 1 | **3,22 : 1** | **−8%** |
| **teste — lógica** | ~186 | **233** | +25% |
| **teste — comentário** | ~278 | **179** | −36% |
| **teste — total** | ~464 | **412** | **−11%** |
| **teste — mistura** | 1,5 : 1 | **0,77 : 1** | **−49%** |
| **total adicionado (não vazio)** | ~572 | **623** | **+9%** |
| removidas (não vazias) | ~10 | **25** | +150% |

#### O VEREDITO DAS DUAS HIPÓTESES

**(a) A mistura de produção: CONFIRMADA, e é a melhor projeção de mistura da série.**

Declarado: ~3,5:1, **falsa abaixo de 2,0:1**, fora de território acima de 5,0:1.
Medido: **3,22:1**, dentro da faixa, com erro de **−8%** no número.

| medição | change | tipo | comentário : lógica na produção |
|---|---|---|---|
| 23ª | `insights-sistema-motivo-da-recusa` | consumo + correção | 1,98 : 1 |
| 24ª | `consumo-por-agente-falhas` | correção | 4,4 : 1 |
| 25ª | `declared-gap-variante-morta` | remoção | 5,0 : 1 |
| **26ª** | **`insights-periodo-entre-telas`** | **passagem entre telas** | **3,22 : 1** (161 : 50) |

A contagem de **cinco registros de mecanismo** sustentou a projeção, e a mistura caiu
entre a 23ª (menos registros) e a 24ª (correção de registros tornados falsos) —
exatamente onde a leitura previa.

**(b) O lugar do desvio: FALSIFICADA.** Declarado: *"o desvio estará no TESTE e será
menor que +43%"*. **O teste errou −11%; a PRODUÇÃO errou +95%.** A cláusula de magnitude
se cumpre no lado previsto, mas **a previsão de lugar está errada**, e pela régua da
própria 25ª — separar direção de magnitude — isso é falsificação, não acerto parcial.

**As duas causas, as duas por leitura e as duas reusáveis:**

1. **Registro de mecanismo que é ARGUMENTO custa o dobro do que é AFIRMAÇÃO.** A
   projeção usou ~17 linhas por registro. Medido: `insightsWindow.ts` entregou **57
   linhas de comentário para 5 de lógica — 11,4 : 1**, porque a D2 não é uma frase, é
   **três razões numeradas com o requisito vivo que a primeira cita**, e a D9 é uma
   **recusa** que precisa carregar o caso da #89 para não ser reaberta. **Régua:
   projetar por registro é grosso demais — contar as RAZÕES CITADAS, com ~26 linhas por
   razão**, que é o custo unitário que a 5a-4 já havia medido para uma recusa de três
   razões e que esta medição confirma de forma independente.
2. **No TESTE a mistura não é a da produção, e aplicar uma só superestima o comentário.**
   Projetado 1,5:1 (o número da 25ª); medido **0,77:1** — menos da metade. A causa é a
   régua da 25ª vista por outro ângulo: *guarda com arranjo próprio custa o dobro* — e
   **o arranjo é lógica sem comentário**. O invólucro controlado de
   `AgentInsightsTab.test.tsx`, o `createMemoryRouter` de `SystemInsightsPage.test.tsx` e
   as contagens de largura são encanamento, não registro. A 25ª mediu 1,53:1 em guardas
   cuja razão de existir **era** uma classe de defeito; aqui metade das linhas é
   montagem. **Régua: projetar a mistura do teste SEPARADA por tipo de guarda — ~1,5:1
   para guarda de classe de defeito, ~0,5:1 para arranjo.**

**E as 25 linhas removidas contra ~10 projetadas** têm causa simples e a mesma família: a
projeção contou os dois `useState` e a linha do escritor, e esqueceu que **trocar um
invólucro de teste remove o antigo** — o `MemoryRouter` de `SystemInsightsPage.test.tsx`
e o bloco de montagem de `renderTab` saíram inteiros.

### O resultado da régua da fiação — PELA DISTRIBUIÇÃO, não pela contagem

**Duas operações, e elas dão respostas OPOSTAS. A previsão do `design.md` se cumpriu:
nenhum guarda extra foi preciso.** Saídas completas em
`~/.cache/buteco-agents/issue-85/fiacao-*.txt`, suíte inteira nas duas, zero
`Unhandled Error`.

| operação | reprovações | onde | é fiação? |
|---|---|---|---|
| tirar `period` do `to=` de `AgentConsumptionCard.tsx` | **4**, em **2 arquivos** | 2 em `AgentConsumptionCard.test.tsx` (próprio) **+ 2 em `router.test.tsx`** | **SIM** — reprova fora do arquivo do consumidor |
| tirar a preservação de `handleTabChange` | **2**, em **1 arquivo** | 2 em `AgentDetailPage.test.tsx` (próprio) | **NÃO** — distribuição local |

**As mensagens, que são o que distingue as duas:**

```
# operação 1 — fora do próprio arquivo (router.test.tsx)
AssertionError: expected [ 30 ] to include 90
AssertionError: expected [ 30 ] to not include 30
# operação 1 — no próprio arquivo (AgentConsumptionCard.test.tsx)
Expected the element to have attribute: .../agents/...?tab=insights&period=90d
AssertionError: expected null to be '7d'

# operação 2 — só no próprio arquivo (AgentDetailPage.test.tsx)
AssertionError: expected null to be '90d'
AssertionError: expected null to be '7d'
```

**A previsão do `design.md` para a operação 2 estava CONDICIONAL e a condição era
falsa.** Ela dizia *"`AgentDetailPage.test.tsx` **e** `router.test.tsx` — sim, **se a
travessia passar por troca de aba**"*. A travessia **não** passa: ela chega com o período
já no endereço e não troca de aba no caminho. Então a distribuição local é o resultado
**certo**, e não uma lacuna — a preservação é mesmo local àquela página, e nenhuma outra
superfície depende dela.

**O que a oposição entre as duas ensina:** a mesma change tem uma costura de fiação
(o link → a aba, atravessando duas features) e uma de disciplina local (o escritor de
`tab`), e **só a distribuição as separa**. A contagem diria "4 contra 2" e não diria
nada: o que decide é 2 arquivos contra 1.

### As rodadas de guarda contra o defeito real (convenção 15)

| guarda | defeito reintroduzido | mensagem da reprovação |
|---|---|---|
| trocar de aba preserva os demais parâmetros | o escritor original, com segundo parâmetro semeado | `expected null to be '90d'` |
| voltar à visão geral remove só a aba | idem | `expected null to be '7d'` |
| a janela atravessa até a aba | `period` fora do `to=` do link | `expected [ 30 ] to include 90` |
| NEGATIVO: a travessia não usa a janela padrão | idem | `expected [ 30 ] to not include 30` |
| endereço SEM período não é reescrito | `useEffect` normalizando o endereço | `expected '?period=30d' to be ''` |
| período inválido não reescreve o endereço | idem | `expected '?period=30d' to be '?period=180d'` |

**E os dois de "não reescrito" foram escritos ANTES de a reescrita ser retirada**, contra
uma normalização deliberada — sem isso eles passariam por construção, que é a quarta forma
da convenção 15.

**Uma reprovação não prevista, e ela é registro de armadilha, não de defeito:** o guarda
`o endereço decide o período da aba` reprovou com `expected 30 to be 90` **com a produção
correta**. O `beforeEach` do bloco `aba Conhecimento` não resetava `getAgentInsights`, e
`mock.calls[0]` trouxe a janela de um teste anterior. O próprio arquivo já documentava o
acúmulo na linha 520 — num espião que a lista de resets não cobria. **Corrigido no
`beforeEach`, com a causa escrita ali.**

## 8. A conferência visual e a documentação — fecham ANTES do archive

- [x] 8.1 (`apps/frontend`) **Conferência manual feita, quadro a quadro, nos dois
      esquemas de cor** (convenção 14), com `apps/api` (5017) e `apps/frontend` (5173)
      contra o banco de dev. Oito quadros:

      | # | quadro | endereço depois | o que a tela diz |
      |---|---|---|---|
      | 1 | `/insights` sem parâmetro | `/insights` — **não reescrito** | seletor `30d`, janela 02/09→02/10 |
      | 2 | `/insights?period=90d` | igual | seletor `90d`, janela **04/07→02/10**, aviso "**91** dias pedidos, 11 com medida, **80** não existem" |
      | 3 | clicar `7d` na página | `?period=7d` | janela 25/09→02/10, **0 tasks**, aviso de medição parcial some (a janela cabe inteira no regime) |
      | 4 | **travessia**: ranking em 90d → clique no nome | `/agents/{id}?tab=insights&period=90d` | aba **ativa e em 90d**, mesma janela 04/07→02/10 e **mesmo aviso 91/80** do ranking |
      | 5 | na aba, ir para "Visão geral" | **`?period=90d`** | `tab` saiu, **período ficou** — é a D5 na tela |
      | 6 | voltar para "Insights" | `?period=90d&tab=insights` | volta **em 90d**, não no padrão |
      | 7 | recarregar o endereço da aba | igual | **90d** preservado |
      | 8 | link antigo `?tab=insights` | **não reescrito** | abre em `30d`, aviso 31/20 |

      **Período inválido conferido nas duas superfícies** — `?period=180d` na aba e
      `?period=banana` na página: as duas abrem em `30d`, sem erro, **sem reescrever o
      endereço**.

      **Os quadros 2, 4 e a travessia foram repetidos no esquema CLARO**, com o
      seletor e o aviso legíveis nos dois.

      **UMA RESSALVA DO DADO, não do código, e ela vale registrada:** o último dado de
      execução do banco de dev é de **24/09** e hoje é **02/10**, então **7d está
      vazio** (ranking sem linha, nada para clicar) e **30d e 90d produzem números
      IDÊNTICOS** — 14 tasks, 3,8 M tokens, 4 agentes no ranking. **A travessia foi
      conferida pela janela ecoada no cabeçalho e pelo aviso de medição parcial**
      (31/20 contra 91/80), que diferem, e **não pelos números**, que coincidem por
      causa do dado. Semear o banco para que os três períodos difiram em número é
      decisão do dono.

- [x] 8.2 **`02-HISTORICO_E_STATUS.md`** — registrar a change, a vigésima sexta
      medição com as categorias e o veredito das hipóteses, e **fechar o item (c)** do
      *"Resíduo de decisão — `fechamento-da-l4`"*, que declarava isto como limitação
      conhecida. **Sem afirmar estado de publicação** — nenhuma frase sobre PR, merge
      ou deploy, que não aconteceram quando isto é escrito.

- [x] 8.3 **`01-ARQUITETURA_E_CONVENCOES.md`** — as DUAS convenções mudaram, e as duas
      estão escritas: a **22** ganhou a **oitava ocorrência** (o critério largo erra para
      CIMA — dois falso positivos citando `InsightsPeriod.MissingBoundMessage`, um tipo do
      `apps/api`, em comentário; a régua passou a ser citar o número do critério
      **conferido item a item**, porque a comparação entre critérios diz o que cada um
      errou e não qual está certo), e a **18** ganhou a **vigésima sexta medição** com as
      três réguas novas: razão citada como unidade de projeção (~26 linhas), mistura do
      teste separada por tipo de guarda (~1,5:1 de classe de defeito contra ~0,5:1 de
      arranjo), e *nem todo guarda tem cenário de spec*. O item original dizia "dois
      candidatos reais, a decidir pela medição" — os dois se confirmaram. Dois
      candidatos **reais**, a decidir pela medição e não de antemão: a **convenção 22**
      ganharia a oitava ocorrência (*o critério largo erra para CIMA, e o número citado
      é o do critério conferido* — os dois falso-positivos de `InsightsPeriod`), e a
      **convenção 18** a vigésima sexta medição, se o veredito de 7.7 disser algo que a
      régua ainda não diz.

- [x] 8.4 **`CHANGELOG.md`** — quatro entradas em `### Changed`, acima da que a
      `fechamento-da-l4` deixou, e a conferência de que nenhuma nasce falsa foi feita: a
      entrada diz que o link **desliza**, não que congela. A entrada, em pt-BR, dizendo o que o operador passa a
      poder fazer. **Conferir que ela não nasce falsa** (convenção 13, última forma): não
      afirmar que o link congela o período, porque ele **desliza** (D2).

- [x] 8.5 **`docs/` varrido PELA FORMA — resultado NEGATIVO, e vale registrado.**
      `docs/` **não menciona Insights uma única vez**, e não menciona `searchParams`,
      parâmetro de consulta nem estado local. O único *"sem seletor de período"* do
      `CHANGELOG` (`:340`) é sobre a tela de **inventário** e segue verdadeiro.
      `scripts/check-docs.py` verde. Procurado por afirmação
      de que o período é escolha local da tela, ou que o endereço das telas de Insights
      não carrega parâmetro. Varredura por `#85` acha o que fala do assunto, não o que o
      executa.

- [x] 8.6 **`KnowledgeBaseDetailPage.tsx:98` virou issue — a #96**, autorizada pelo dono e
      aberta **antes** do apply, com os três rótulos (`app: frontend`,
      `tipo: débito técnico`, `aguardando gatilho`) e em `Backlog`, onde o workflow a pôs
      sozinho. O corpo diz as três coisas: que a irmã foi corrigida na #85 e por que esta
      ficou de fora, o **gatilho observável** (o primeiro parâmetro de consulta além de
      `tab` naquele endereço), e **a classe** — `setSearchParams` escrevendo o objeto
      inteiro, com sintoma de **perda silenciosa de escolha do operador**. Convenção 23:
      *o que não tem issue não existe depois do archive.*

- [x] 8.7a `openspec validate --all` verde. **O sincronismo das specs é do archive**, que
      não foi feito — a change está pausada esperando a conferência visual do dono (8.1).

- [x] 8.8 **Nenhum PR de cauda, nenhum commit de cauda** (convenção 24): a documentação
      inteira — `01`, `02`, `CHANGELOG` e a varredura de `docs/` — fechou AQUI, antes do
      archive, e nada dela ficou para depois do merge. A documentação
      inteira entra **nesta** change, antes do archive. O único pós-merge é conferir que
      o `Closes #85` fechou a issue e levou o cartão a `Done`.

---

**Ordem de dependência:** 1 → 2 → 3 → 4 e 5 (independentes entre si, as duas depois
do 3) → 6 (depende de 4.2 e 5.2) → 7 (depende de tudo verde) → 8.

**Nada é commitado sem autorização explícita do dono.** O archive precede o push e a
abertura do PR (convenção 24), e o PR carrega `Closes #85`.
