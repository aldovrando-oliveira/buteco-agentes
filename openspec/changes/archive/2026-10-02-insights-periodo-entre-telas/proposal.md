**Issue:** #85

## Why

**O período escolhido pelo operador não viaja entre as duas telas de Insights.**
As duas o guardam em estado local semeado com o mesmo default — conferido no
`HEAD` desta branch:

- `apps/frontend/src/features/insights/pages/SystemInsightsPage.tsx:146` —
  `useState<InsightsPeriod>(DEFAULT_INSIGHTS_PERIOD)`;
- `apps/frontend/src/features/insights/components/AgentInsightsTab.tsx:150` —
  idem.

Nenhuma das duas lê nem escreve o período em `searchParams`. A passagem entre
elas é um `Link` comum (`AgentConsumptionCard.tsx:239`), então quem comparou
agentes em **90 dias** na página do sistema e clica no nome de um deles **abre a
aba na janela padrão de 30 dias — sem nada na tela dizendo que a janela mudou.**

**E isso deixa pela metade trabalho já entregue.** A `fechamento-da-l4` fez o
clique ser um só (`?tab=insights`), e a decisão da #67 se apoia exatamente em *"a
profundidade está a um clique"*. Um clique que troca a janela de medição em
silêncio não é a mesma passagem: os dois números existem, os dois estão certos, e
**eles não falam do mesmo período**. A D7 daquela change registrou isto como
limitação conhecida dela, com o ponteiro para esta issue.

É a forma mais silenciosa de erro desta linha de trabalho — nada está errado na
tela, e tudo está respondendo outra pergunta.

**Por que agora:** a #85 é a última da fila ordenada de `Ready`
(`02-HISTORICO_E_STATUS.md`, *"A coluna `Ready` em ordem de execução"*), e as
quatro à frente dela fecharam — #75 (PR #91), #84/#86 (PR #93) e #83/#89
(PR #95). O gatilho registrado no `02` para o item **(e)** da etapa 5 era *"o
primeiro pedido de compartilhar link da aba"*; ele foi **superado por um caso mais
forte** e absorvido pela #85 em 26/09/2026.

## What Changes

Tudo em **`apps/frontend`**. Nenhum arquivo de `apps/api`, `apps/workers`,
`apps/inbox` ou `deploy/` é tocado, e nenhuma rota de backend muda.

- **O período passa a morar no endereço, como parâmetro de consulta** — `?period=`
  carregando o **nome da janela** (`7d`, `30d`, `90d`), não os limites absolutos.
  O motivo da escolha entre as duas formas está na D2 do `design.md`, e ela decide
  **qual pergunta o link responde**.
- **As duas superfícies leem o período do endereço** em vez de estado local: a
  página do sistema (`/insights?period=90d`) e a aba do agente
  (`/agents/<id>?tab=insights&period=90d`).
- **A troca de período escreve o endereço**, então ela sobrevive a recarregamento
  e viaja em link colado.
- **O link do ranking carrega o período** — ele atravessa para um endereço
  diferente, onde o parâmetro não é herdado por si.
- **A troca de aba deixa de apagar o resto do endereço.** Hoje
  `AgentDetailPage.tsx:76` faz `setSearchParams(next === OVERVIEW_TAB ? {} : { tab: next })`,
  que **substitui a busca inteira** — com o período no endereço, trocar de aba e
  voltar o perderia em silêncio. O escritor passa a mexer **só na chave que lhe
  pertence**. (Era defeito latente, não observável: hoje aquele endereço não tem
  segundo parâmetro.)
- **Ausência e valor desconhecido caem no default sem reescrever o endereço** —
  o mesmo contrato que `parseTab` já declara para a aba, pelo mesmo motivo (não
  poluir o histórico de navegação). Link antigo, sem período, continua abrindo em
  30 dias.
- **A janela continua nascendo dentro do `queryFn`**, no instante da consulta, e
  **continua fora da chave de cache**. O que muda é só a origem da escolha do
  operador, nunca o momento do cálculo.

Nada de **BREAKING**: a ausência do parâmetro é a forma canônica, e todo endereço
já compartilhado continua válido com o mesmo significado que tinha.

## Capabilities

### New Capabilities

Nenhuma. A change não cria capacidade — ela torna endereçável uma escolha que
três requisitos vivos já descrevem.

### Modified Capabilities

- `system-insights-ui`: o requisito *"Janela do período escolhida pelo operador"*
  passa a exigir que a escolha seja **endereçável** — lida e escrita no endereço,
  sobrevivendo a recarregamento e a link compartilhado —, e que o link do ranking
  a **carregue** para a superfície de destino. O requisito *"Consumo por agente
  cruza com o catálogo e leva ao diagnóstico"* ganha a cláusula de que a passagem
  de um acionamento só preserva a janela, porque é a janela que faz a comparação
  entre as duas telas ser válida.
- `agent-insights-ui`: o requisito *"Janela, regime e cobertura da faixa medida na
  aba"* passa a exigir que o período venha do **endereço** e não de estado local,
  com o mesmo vocabulário de ausência e de valor desconhecido da aba.
- `agent-catalog-ui`: o requisito *"Abas do detalhe do agente"* ganha a cláusula de
  que mudar de aba **preserva os demais parâmetros** do endereço — a aba é uma
  chave do endereço, não o endereço inteiro.

## Impact

**App afetado: `apps/frontend` apenas.**

Arquivos de produção alcançados — **cinco, todos modificados, nenhum criado**
(blast radius lido no código antes de projetar, convenção 18):

| arquivo | o que muda |
|---|---|
| `src/features/insights/utils/insightsWindow.ts` | ganha a leitura do parâmetro, e é onde a decisão preset × instantes fica registrada |
| `src/features/insights/pages/SystemInsightsPage.tsx` | estado local → endereço |
| `src/features/insights/components/AgentInsightsTab.tsx` | estado local → prop recebida da página |
| `src/features/agents/pages/AgentDetailPage.tsx` | passa a ler o período do endereço, a repassá-lo e a preservar o resto da busca na troca de aba |
| `src/features/insights/components/AgentConsumptionCard.tsx` | o link do ranking carrega a janela |

**Consumidores conferidos pelo `tsc`, não por leitura**: `<AgentInsightsTab` é
montado em 2 lugares (`AgentDetailPage.tsx` e o teste dele) e
`<AgentConsumptionCard` também em 2 (`SystemInsightsPage.tsx` e o teste dele) —
os quatro já estão na lista acima ou na de testes.

Arquivos de teste alcançados — **seis, todos modificados** (régua "todo arquivo
modificado arrasta o teste dele", projetado em pares):
`insightsWindow.test.ts`, `SystemInsightsPage.test.tsx`,
`AgentInsightsTab.test.tsx`, `AgentDetailPage.test.tsx`,
`AgentConsumptionCard.test.tsx` e `src/app/router.test.tsx` — este último porque
é onde vive o guarda que **navega**, e guarda que afirma o meio do caminho não
prova o fim dele.

**Sem dependência nova.** Nenhuma versão de runtime, framework ou biblioteca é
fixada ou alterada: `useSearchParams` do `react-router` já é usado em duas páginas
do painel, e `SegmentedControl` do Mantine continua sendo o seletor.

**Nenhum dado novo é coletado e nenhuma rota é chamada de forma diferente.** As
duas rotas agregadas continuam recebendo `from` e `to` absolutos, calculados no
instante da consulta como hoje.
