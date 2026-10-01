**Issue:** #75 (fecha) · **#80 dobrada dentro dela** (fechada em 26/09, conteúdo
exclusivo movido antes) · **Etapa de consumo da #51**, sequenciada pela convenção
1 · **Achados com issue própria:** ver *Achados* abaixo

## Why

**O dado está sendo coletado em produção desde o deploy da #51 e não chega à
tela.** `GET /insights/system` serve `rejectionRegime`,
`rejectedAtEntryCount` e `rejectionsByReason` — conferido no `HEAD` em 27/09 em
`SystemInsightsResponse.cs:189` e no handler —, e
`apps/frontend/src/features/insights/types/systemInsights.ts` **não tem nenhum
dos três**. É a única issue da fila em que algo já medido está invisível; as
outras quatro são número errado ou ausência.

E a tela não está apenas incompleta: ela está **contraditória**. O bloco
*"Recusadas na entrada"* do `FailuresCard` apresenta `errors.rejectedCount`, que
é **a outra população** — a recusa **com** linha de execução, hoje só a de
profundidade de delegação. Enquanto era o único número de recusa da tela, o
rótulo era impreciso. Com os dois números medidos e servidos, ele passa a
nomear um como se fosse o outro.

**E a spec do card de Motivos contradiz a implementação** — contradição em main
spec, da mesma família que a `fechamento-da-l4` acabou de resolver, e permanente
até alguém corrigir: o requisito manda apresentar a recusa *"como lacuna
declarada — a contagem, mais o texto que diz que o motivo não é coletado"*, e o
guarda `NEGATIVO: a recusa NÃO vira quadro no card de Motivos` afirma que a
palavra *recusa* **não aparece** no card. A spec ficou na versão pré-decisão da
décima rodada da #52 (convenção 9).

## O que a reconferência contra o `HEAD` mudou nas premissas da issue

A #75 foi escrita antes da `insights-aba-do-agente` e da `fechamento-da-l4`, e as
duas tocaram estes arquivos. **Três premissas dela não valem mais, e uma vale a
mais do que ela dizia.**

**1. `utils/rejectionReasonLabels.ts` NÃO EXISTE.** A #80 diz *"o vocabulário já
está traduzido em `utils/rejectionReasonLabels.ts`, escrito pela #53 — este seria
o segundo consumidor dele, e nenhum código novo é necessário para isso"*. O
arquivo não está no `HEAD` e nunca esteve: a `insights-aba-do-agente` **removeu**
a recusa de entrada e os motivos do `AgentFailuresCard` em vez de traduzi-los,
por decisão do dono na tela — a D6 e a D10 daquela change se contradiziam, e o
artboard do agente não tem elemento para eles (`AgentFailuresCard.tsx:24-43`).

**A consequência é de convenção, não de arquivo:** esta é a **primeira**
superfície a traduzir o vocabulário de motivo de recusa, não a segunda. **O
gatilho da convenção 2 não foi atingido**, e o módulo que nasce aqui é o lugar
onde o código mora, não extração de repetição observada. Ver D5 do `design.md`.

**2. O `caveat` morto já saiu — e sobrou o lugar dele.**
`rejection-reason-not-collected` não está mais em `caveatLabels.ts`, com o
comentário registrando a causa e os testes de `apps/api` afirmando a ausência nos
dois handlers. **Item da issue cumprido, marcado e não refeito.** O que ficou é
resíduo: o tipo `CaveatPlacement` ainda declara `'rejection-reason'`
(`caveatLabels.ts:68-69`), e **nenhum código mapeia código nenhum para ele** —
membro de união morto, da mesma família do branch morto da #83.

**3. `REGIMES_FIXTURE` tem dois regimes e a rota serve três.** O duplo
(`systemInsightsFixture.ts:118-122`) declara `execution` e `embedding`; a rota
serve os três de `MetricsOptions.All`, e `rejection` é obrigatório. É por isso
que o cenário *"Regime novo é absorvido sem mudança de estrutura"* passa hoje: a
tela nunca viu um terceiro.

**4. E o terceiro regime é a primeira vez que aquele requisito é exercitado de
verdade — e ele quebra no rótulo.** `regimeNoteFor`
(`SystemInsightsPage.tsx:163`) monta o texto com
`name === insights.tokens.embeddingRegime ? 'embedding' : name`: qualquer regime
que não seja o de embedding cai no **próprio nome do fio**. Pôr a recusa na tela
hoje escreveria, em português, *"rejection medido desde 26/09/2026"*.

## What Changes

Toda em `apps/frontend` mais registro. **Nenhuma linha em `apps/api`** — os três
campos já são servidos desde a #51.

- **Os três campos entram no contrato transcrito** e na `systemInsightsFixture`,
  com `RejectionReasonCount` no mesmo formato de `FailurePhaseCount`, que é o que
  a rota escolheu de propósito.
- **O bloco *"Recusadas na entrada"* passa a ler `rejectedAtEntryCount`.** O
  rótulo e o número do `Main.dc.html` são os dessa população — o artboard escreve
  `5` ali e `5` na linha de motivo do card vizinho, e são o mesmo fato.
- **`rejectedCount` sai da tela do sistema**, e isso é decisão registrada, não
  esquecimento: o `Main.dc.html` não tem elemento para ela — não há KPI de taxa
  de falha nesta página —, e ocupar o rótulo da outra população era o defeito.
  Vai para *"servido e não desenhado"*, com issue e gatilho. Ver D3.
- **Os motivos entram no card de Motivos, em grupo próprio dentro do mesmo
  card** — não como sexta linha da lista rasa que o artboard desenha. O artboard
  mistura as duas populações numa lista ordenada única porque foi desenhado antes
  de elas existirem separadas; uma lista em que a linha 1 é fase de execução e a
  linha 2 é motivo de recusa, sem marca nenhuma, convida a somar exatamente o que
  o texto do card vizinho proíbe em palavras. **Divergência registrada
  (convenção 17)**, com a forma que o `AgentFailuresCard` já usa. Ver D2.
- **Motivo desconhecido aparece cru**, e o guarda afirma que ele **aparece**: a
  coluna é obrigatória na fonte e a soma dos motivos fecha com
  `rejectedAtEntryCount`; omitir um faria a soma deixar de fechar **sem
  sintoma**.
- **O regime da recusa é declarado no número dele**, com rótulo de operador. Os
  três regimes conhecidos ganham rótulo; a queda para o nome cru permanece — e
  passa a ser **deliberada**, para o regime que a tela não conhece, com guarda.
- **O texto *"Recusa é agente inativo ou sem provider e modelo configurados"*
  enumera duas das quatro causas.** O vocabulário coletado tem quatro —
  `AgentInactive`, `ProviderOrModelMissing`, `ProviderNotConfigured` e
  `AgentNotFound` —, e com os motivos na lista ao lado a frase estaria enumerando
  um subconjunto do que a tela mostra. É a convenção 13 na forma *"verdadeira
  quando escrita, e outra etapa tornou falsa"*.
- **O membro morto `'rejection-reason'` sai de `CaveatPlacement`**, com a causa.
- **Dois guardas negativos existentes invertem**, e isso não os enfraquece: ver
  D4. O que eles protegem — a tela nunca nomeia causa que ninguém mediu —
  sobrevive; a forma muda, porque a forma afirmava a ausência de uma palavra que
  agora é medição.

### O que NÃO muda

`apps/api`, `apps/workers`, `apps/inbox`, o `nginx.conf`, e **a aba do agente**,
que acabou de ir a produção — a decisão de lá (a recusa de entrada fora do card)
continua valendo, e é o artboard que separa os dois casos.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `system-insights-ui`: dois requisitos mudam, e um deles é a contradição.
  - *"Motivos de falha, com o motivo da recusa declarado como lacuna"* —
    **RENAMED e MODIFIED**. O título promete a lacuna que a change fecha. Cai o
    `SHALL` de apresentar a recusa *"como lacuna declarada — a contagem, mais o
    texto que diz que o motivo não é coletado"* e cai o cenário *"Recusa entra
    como lacuna declarada"*. Entra: os motivos como grupo **separado** das fases,
    o motivo desconhecido apresentado cru, a soma fechando com a contagem de
    recusa de entrada, e o `SHALL NOT` de apresentar um total único das duas
    populações. **O `SHALL NOT` de nomear causa não medida permanece** — é o que
    a change preserva, com fonte em vez de com silêncio.
  - *"Falha e recusa são apresentadas separadas, e a recusa não vira
    percentual"* — **MODIFIED**. A contagem de recusa que o requisito manda
    apresentar passa a ser, explicitamente, **a da recusa de entrada**, com o
    regime dela declarado junto do número. A recusa **com** linha de execução
    deixa de ser apresentada nesta superfície, e a spec diz por quê.

## Impact

**`apps/frontend`** — e só ele.

Criados (2):

- `src/features/insights/utils/rejectionReasonLabels.ts`
- `src/features/insights/utils/rejectionReasonLabels.test.ts`

Modificados (10, em pares de produção + teste onde o par existe):

- `src/features/insights/types/systemInsights.ts` — `RejectionReasonCount`, os
  três campos, e o comentário de `rejectedCount` (*"Sem motivo — é a lacuna L1
  (#51)"*, que deixou de ser verdade)
- `src/features/insights/test/systemInsightsFixture.ts` — os três campos em
  `errorsFixture`, e o terceiro regime em `REGIMES_FIXTURE`
- `src/features/insights/components/FailuresCard.tsx` + `.test.tsx` — a troca de
  campo, o regime declarado, o texto das quatro causas, o comentário do topo
- `src/features/insights/components/FailureReasonsCard.tsx` + `.test.tsx` — o
  grupo de motivos, e o comentário do topo, que hoje descreve a L1 como aberta
- `src/features/insights/pages/SystemInsightsPage.tsx` + `.test.tsx` — os três
  campos em `CORPO_AUSENTE`, o rótulo dos regimes, e a nota que chega aos cards
- `src/features/insights/utils/caveatLabels.ts` + `.test.ts` — o membro morto da
  união, e o comentário de conferência da rota

**Registro:**

- `openspec/specs/system-insights-ui/spec.md`, pelo delta desta change
- `02-HISTORICO_E_STATUS.md` — a decisão, a divergência do artboard com a causa,
  a vigésima terceira medição da convenção 18, e o destravamento do índice do
  board
- `CHANGELOG.md`

### Achados desta change, com issue própria (convenção 23)

Nenhum é trabalho daqui (convenção 1).

1. **`rejectedCount` sai da tela do sistema e precisa de issue com gatilho** —
   *servido e não desenhado*, junto do resíduo, da profundidade de delegação e
   das tasks sem estado terminal. **Gatilho:** o primeiro pedido do dono por um
   número de recusa com linha de execução na página do sistema, ou o `Main.dc.html`
   ganhar elemento para ele. Sem issue, a saída dele vive só num `design.md` que o
   archive apaga.
2. **O membro `'rejection-reason'` de `CaveatPlacement` é união morta** —
   corrigido **aqui**, porque é esta change que teria sido a consumidora dele e
   não é, e porque a correção é uma linha no arquivo que a change já abre. Fica
   registrado como achado porque a **forma** é a da #83, e a #83 mostra que a
   forma se repete: declaração sem consumidor que o compilador não acusa.
3. **A #84 e a #86 foram reconferidas de passagem, e as duas continuam
   valendo** — `AgentConsumptionCard.tsx:65` monta
   `new Map(failuresByAgent.map(f => [f.agentId, f.failedCount]))` e guarda a
   última por `agentId` (#86); as linhas nascem de `tokens.byAgent`, então agente
   que falhou sem chamar o provedor não tem linha (#84, `:108`). **Nada corrigido
   aqui** — as duas são uma change só, adjacente na fila, e corrigir uma deixa o
   número errado de outro jeito.
4. **`ProviderConsumptionCard` declara um regime entre duas populações** — a mesma
   forma do defeito que a conferência do dono achou no card de Motivos, e a razão de
   não ter sido corrigida junto: ali o regime nomeado (`embedding`) tem uma **coluna
   com o próprio nome** logo abaixo, então o leitor amarra a nota à coluna. No card
   de Motivos não havia amarração nenhuma. **Achado, não escopo** — registrado na
   **#90**, que é também onde mora a saída da nota por grupo.
5. **O comentário de conferência de `caveatLabels.ts:44` conta errado** — diz
   *"5 códigos em 2 blocos — `performance` (2), `errors` (2)"*, e 2 + 2 são
   **4**. Recontado no handler: `performance` emite dois
   (`GetSystemInsightsQueryHandler.cs:529`) e `errors` emite dois (`:698-699`),
   total **4**. O `5` é da contagem anterior à #51, quando `errors` tinha três.
   Corrigido no mesmo arquivo que a change já abre — e é a convenção 22 na forma
   da sexta ocorrência: número que **nasceu errado**, com o gatilho dele vencido
   no dia em que foi escrito.
