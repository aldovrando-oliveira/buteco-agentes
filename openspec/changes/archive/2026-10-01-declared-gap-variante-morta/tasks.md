**Todas as tarefas rodam em `apps/frontend`**, salvo as de documentação, que são de
raiz do repositório. **Nenhuma toca `apps/api`, `apps/workers` ou `apps/inbox`.**

**Nada é commitado.** O commit, o push e o PR são decisão do dono.

## 0. A conferência de escopo — a lista fechada, montada por varredura

A saída abaixo é **colada**, não redigitada. Rodada em 01/10/2026 na branch
`fix/83-declared-gap-variante-morta`, que nasce de `9709e045`.

```
$ git log --oneline -1 && git rev-parse HEAD
9709e04 Merge pull request #93 from aldovrando-oliveira/fix/84-consumo-por-agente-falhas
9709e0458bf7179b7af12a588e3dd23272399b2a

$ git merge-base --is-ancestor 9709e045 HEAD && echo "a branch nasce da main com a #84/#86"
a branch nasce da main com a #84/#86

$ grep -rn "<DeclaredGap" apps/frontend/src
apps/frontend/src/features/insights/components/InsightsKpiGrid.tsx:92:          <DeclaredGap
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx:11:      <DeclaredGap
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx:73:        <DeclaredGap
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx:108:        <DeclaredGap
apps/frontend/src/features/insights/components/DeclaredGap.tsx:55:// `<DeclaredGap>` novo sem a prop renderiza exatamente o quadro que a spec
apps/frontend/src/features/insights/components/AgentModelsCard.tsx:148:          <DeclaredGap

$ grep -rn -B2 "variant=" .../InsightsKpiGrid.tsx .../AgentModelsCard.tsx
InsightsKpiGrid.tsx-91-        footer={
InsightsKpiGrid.tsx-92-          <DeclaredGap
InsightsKpiGrid.tsx:93:            variant="inline"
AgentModelsCard.tsx-147-
AgentModelsCard.tsx-148-          <DeclaredGap
AgentModelsCard.tsx:149:            variant="inline"

$ grep -rn "data-gap-variant" apps/frontend/src
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx:87:    expect(lacuna).toHaveAttribute('data-gap-variant', 'inline');
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx:101:    expect(lacuna).toHaveAttribute('data-gap-variant', 'block');
apps/frontend/src/features/insights/components/DeclaredGap.tsx:96:      <Box data-testid={testId} data-declared-gap="true" data-gap-variant="inline">
apps/frontend/src/features/insights/components/DeclaredGap.tsx:106:      data-gap-variant="block"
apps/frontend/src/features/insights/components/AgentModelsCard.test.tsx:132:      'data-gap-variant',
apps/frontend/src/features/insights/components/InsightsKpiGrid.test.tsx:68:    expect(lacuna).toHaveAttribute('data-gap-variant', 'inline');

$ grep -rln "DeclaredGap" apps/frontend/src --include="*.ts" --include="*.tsx"
apps/frontend/src/features/insights/components/DeclaredGap.test.tsx
apps/frontend/src/features/insights/components/InsightsKpiGrid.tsx
apps/frontend/src/features/insights/components/DeclaredGap.tsx
apps/frontend/src/features/insights/components/AgentModelsCard.tsx
```

**E as duas varreduras não dão a mesma lista — é por isso que são duas.** A busca
pelo **nome do componente** devolve **4** arquivos; a busca pelo **atributo**
acrescenta `InsightsKpiGrid.test.tsx` e `AgentModelsCard.test.tsx`, que afirmam
`data-gap-variant` **sem nomear `DeclaredGap`**. É a mesma régua que a #84/#86
registrou — *varredura por número de issue acha o que fala do assunto, não o que o
executa* —, agora na forma *varredura por nome de componente perde quem toca só a
marca que ele emite*.

**A lista fechada do escopo é de 7 arquivos:**

| # | arquivo | por quê |
|---|---|---|
| 1 | `src/features/insights/components/DeclaredGap.tsx` | a prop, o branch, o atributo e dois blocos de comentário |
| 2 | `src/features/insights/components/DeclaredGap.test.tsx` | 2 guardas novos, 1 removido, 1 adaptado |
| 3 | `src/features/insights/components/InsightsKpiGrid.tsx` | a prop sai da chamada (`:93`) |
| 4 | `src/features/insights/components/InsightsKpiGrid.test.tsx` | o guarda de `data-gap-variant` (`:68`) |
| 5 | `src/features/insights/components/AgentModelsCard.tsx` | a prop sai da chamada (`:149`) |
| 6 | `src/features/insights/components/AgentModelsCard.test.tsx` | o guarda de `data-gap-variant` (`:132`) |
| 7 | `src/features/insights/utils/caveatLabels.test.ts` | o guarda exaustivo da #89 |

**Mais a documentação de raiz:** `01-ARQUITETURA_E_CONVENCOES.md`,
`02-HISTORICO_E_STATUS.md`, `CHANGELOG.md` e o delta de
`openspec/specs/system-insights-ui/spec.md`.

- [x] 0.1 Conferir, antes de abrir qualquer arquivo, que a árvore não cresceu além
      dos 7 — rodar as duas varreduras de novo e comparar com a lista acima
      (`apps/frontend`)

### A varredura da #89 — os 30 aliases, colados

```
$ grep -rn "^\s*\(export \)\?type [A-Za-z0-9_]\+ *=" apps/frontend/src \
    --include="*.ts" --include="*.tsx" | grep -v "\.test\." | wc -l
      30

$ ... | sed "s|apps/frontend/src/||" | sort
components/brand/Logo.tsx:6:type LogoVariant = 'mark' | 'symbol' | 'vertical';
components/brand/Logo.tsx:8:type LogoProps = {
features/agents/components/AgentToolsTab.tsx:20:type Links = Record<string, string[]>;
features/agents/pages/AgentDetailPage.tsx:27:type AgentDetailTab =
features/agents/pages/AgentListPage.tsx:24:type StatusFilter = typeof ALL | typeof ACTIVE | typeof INACTIVE | typeof NEEDS_RECONFIGURATION;
features/agents/types/agent.ts:78:export type UpdateAgentInput = CreateAgentInput;
features/channels/types/channel.ts:1:export type ChannelType = 'waha' | 'telegram';
features/insights/utils/caveatLabels.ts:105:export type CaveatSurface = 'system' | 'agent';
features/insights/utils/caveatLabels.ts:88:export type CaveatPlacement =
features/insights/utils/heatScale.ts:19:export type HeatStep = 0 | 1 | 2 | 3 | 4 | 5;
features/insights/utils/insightsWindow.ts:20:export type InsightsPeriod = (typeof INSIGHTS_PERIODS)[number];
features/insights/utils/measuredDays.ts:26:export type DayState = 'measured' | 'measured-zero' | 'unmeasured';
features/insights/utils/metricState.ts:47:export type MetricState = 'value' | 'empty' | 'unknown' | 'zero';
features/insights/utils/metricState.ts:72:export type QueryState = 'ok' | 'loading' | 'failed';
features/insights/utils/metricState.ts:74:type Nullish = number | null | undefined;
features/knowledge-bases/components/DocumentFileList.tsx:19:export type FileSendState =
features/knowledge-bases/components/KnowledgeDocumentModal.tsx:21:type Mode = 'upload' | 'manual';
features/knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:43:type KnowledgeBaseDetailTab = typeof DOCUMENTS_TAB | typeof DIAGNOSTICS_TAB;
features/knowledge-bases/types/knowledgeBase.ts:26:export type UpdateKnowledgeBaseInput = CreateKnowledgeBaseInput;
features/knowledge-bases/types/knowledgeDocument.ts:11:export type KnowledgeIndexingStatus = 'Pending' | 'Indexing' | 'Indexed' | 'Failed';
features/knowledge-bases/types/knowledgeDocument.ts:63:export type UpdateKnowledgeDocumentInput = CreateKnowledgeDocumentInput;
features/knowledge-bases/utils/documentUpload.ts:32:export type SelectedFile =
features/knowledge-bases/utils/indexingSummary.ts:148:export type StatusFilter =
features/mcp-servers/types/mcpServer.ts:1:export type McpServerAuthType = 'None' | 'BearerToken';
features/mcp-servers/types/mcpServer.ts:22:export type UpdateMcpServerInput = CreateMcpServerInput;
features/mcp-servers/types/mcpServer.ts:30:export type McpConnectionTestFailureReason =
features/sessions/types/session.ts:1:export type MessageDirection = 'Inbound' | 'Outbound';
features/sessions/types/session.ts:2:export type MessageDeliveryStatus = 'Sent' | 'Failed';
features/sessions/types/session.ts:3:export type MessageDispatchStatus = 'Pending' | 'Dispatching' | 'Failed' | 'Completed';
features/sessions/types/session.ts:4:export type MessageContentType = 'Text' | 'Image' | 'Audio' | 'Document';
```

**A classificação dos 30 e o resultado estão na D5.** O que esta saída prova é o
**universo**: 30, e não os 19 que o critério estreito da primeira passagem devolveu.
Os membros de contagem mais baixa, abertos um a um:

```
$ grep -rn "'symbol'" apps/frontend/src --include='*.ts' --include='*.tsx'
components/brand/Logo.test.tsx:113:  it.each(['symbol', 'vertical'] as const)(
components/brand/Logo.tsx:6:type LogoVariant = 'mark' | 'symbol' | 'vertical';
components/brand/Logo.tsx:59:  const asked = variant === 'vertical' && size < VERTICAL_MIN ? 'symbol' : variant;

$ grep -rn "'manual'" apps/frontend/src --include='*.ts' --include='*.tsx'
features/knowledge-bases/components/KnowledgeDocumentModal.tsx:21:type Mode = 'upload' | 'manual';
features/knowledge-bases/components/KnowledgeDocumentModal.tsx:60:    setMode(document ? 'manual' : 'upload');
features/knowledge-bases/components/KnowledgeDocumentModal.tsx:218:    if (mode === 'manual') return 'Adicionar documento';
features/knowledge-bases/components/KnowledgeDocumentModal.tsx:246:              { value: 'manual', label: 'Escrever manualmente' },

$ grep -rn "'measured-zero'" apps/frontend/src --include='*.ts' --include='*.tsx'
features/insights/utils/measuredDays.test.ts:41:    expect(dia22?.state).toBe('measured-zero');
features/insights/utils/measuredDays.ts:26:export type DayState = 'measured' | 'measured-zero' | 'unmeasured';
features/insights/utils/measuredDays.ts:98:      state: point.taskCount === 0 ? 'measured-zero' : 'measured',
features/insights/components/DailyTasksCard.test.tsx:113:    expect(ponto).toHaveAttribute('data-day-state', 'measured-zero');
features/insights/components/PeriodHeatmapCard.tsx:97:                  data-cell={naoMedido ? 'unmeasured' : dia.state === 'measured-zero' ? 'zero' : 'measured'}
```

**Os três têm sítio de PRODUÇÃO**, não só de teste — que é a distinção que a #83
torna obrigatória: só de teste é exatamente o caso dela.

- [x] 0.2 Reconferir a varredura da #89 antes de escrever o guarda da 2.5 — ela é de
      01/10/2026 sobre `9709e045`, e o escopo vai colado ao número (`apps/frontend`)

## 1. A baseline, antes de qualquer edição

A primeira tentativa de 01/10/2026 (01:29) **foi abortada**: `load average` de
**33-37** numa máquina de 12 núcleos, a VM do Podman em **343% de CPU**, **2
`Unhandled Error` de `[vitest-pool]: Failed to start forks worker`** e **11 arquivos
reprovados por timeout de worker** — total observado de 1359 contra as 1411
registradas. Ver a D10.

### A remedição FECHOU — `apps/frontend` **1411 / 118**, zero falhas

Segunda tentativa, 01/10/2026 às **21:36:41**, na branch, com a árvore limpa salvo os
artefatos desta change (`?? openspec/changes/declared-gap-variante-morta/`).

**O regime, declarado ao lado do número (convenções 19 e 22):**

```
$ uptime                      # imediatamente ANTES de rodar
21:36  up 26 days,  7:29, 2 users, load averages: 11.39 11.13 7.41
$ sysctl -n hw.ncpu
12
$ podman ps
2d5030400684  docker.io/pgvector/pgvector:pg18           Up 7 days (healthy)
726e5d25722d  docker.io/library/rabbitmq:4.3-management  Up 7 days (healthy)
$ podman machine list
podman-machine-default*  applehv  Currently running  6 CPUs  6GiB  100GiB
# com.apple.Virtualization.VirtualMachine FORA dos 5 maiores consumidores de CPU

 Test Files  118 passed (118)
      Tests  1411 passed (1411)
   Start at  21:36:43
   Duration  112.35s (transform 17.39s, setup 67.15s, import 246.85s, tests 438.83s, environment 387.54s)

$ grep -c "Unhandled Error|Failed to start forks worker|Timeout waiting for worker"
0
```

**Baseline: `apps/frontend` 1411 / 118**, medida em 01/10/2026 às 21:36 sobre
`9709e045` com `load average` de **11,39** em 12 núcleos e a VM do Podman ociosa.
Bate **exatamente** com a registrada pela #84/#86.

**E o contraste entre as duas tentativas é o dado**, não ruído: **mesma árvore,
mesmo commit** — 112 s contra **2379 s**, e **1411 / 0 falhas** contra **1359 / 14**.
**21× de diferença de duração** só por regime de máquina. Nenhum dos 14 "defeitos" da
primeira existia.

- [x] 1.1 Conferir a árvore limpa (`git status --porcelain` só com os artefatos
      desta change) e declarar `podman ps`, `podman machine list` e `uptime` **antes**
      de rodar (raiz)
- [x] 1.2 Rodar `npm test -- --run` em `apps/frontend` **guardando a saída inteira**,
      não só o rodapé — a tentativa abortada perdeu a lista de arquivos reprovados
      por ter passado por `tail` (`apps/frontend`)
- [x] 1.3 Se houver **qualquer** `Unhandled Error` de pool, ou `load average` acima de
      ~12, **abortar e repetir**. Medição inválida citada depois vale menos que
      medição ausente (`apps/frontend`)
- [x] 1.4 Registrar a baseline — `casos / arquivos` — com o `podman ps` e o
      `load average` **na mesma frase** (convenções 19 e 22) (raiz)

## 2. Os guardas, escritos e vistos REPROVAR contra o `HEAD`

**A ordem é a convenção 15, e ela não é negociável aqui:** *tirar a prop no mesmo
passo em que se escreve o guarda o faz passar por construção.* Nada do grupo 3
começa antes de o grupo 2 fechar.

- [x] 2.1 Escrever o **guarda central** em `DeclaredGap.test.tsx`: `<DeclaredGap>`
      **sem escolher forma** não renderiza moldura — `style` sem `dashed`, e
      `data-declared-gap="true"` presente (`apps/frontend`)
- [x] 2.2 Rodar **só esse caso** contra o `HEAD` e **colar a reprovação** — no `HEAD`
      o default é `block` e o estilo tem `border: '1px dashed …'` (`apps/frontend`)
- [x] 2.3 Escrever a **varredura estática** em `DeclaredGap.test.tsx`: a fonte de
      `DeclaredGap.tsx` não declara borda nenhuma. Seguir a forma de
      `components/data/surfaceTokens.test.ts` — ler o arquivo, filtrar as linhas
      infratoras e comparar com `[]`, para a falha **nomear a linha** (`apps/frontend`)
- [x] 2.4 Rodar e **colar a reprovação** dela contra o `HEAD` (`apps/frontend`)
- [x] 2.5 Escrever o **guarda exaustivo da #89** em `caveatLabels.test.ts`: um
      `Record<CaveatPlacement, 'render' | 'nao-render'>` **exaustivo**, e a asserção de
      que toda posição `'render'` tem ao menos um código de `KNOWN_CAVEAT_CODES`
      apontando para ela, nas duas superfícies (`apps/frontend`)
- [x] 2.6 **Reintroduzir `'rejection-reason'`** na união de propósito, rodar, **colar
      a reprovação**, e desfazer. Sem isso o guarda nasce verde por construção, que é
      o defeito que a convenção 15 existe para pegar (`apps/frontend`)

### As reprovações, coladas

**2.2 — o guarda central, contra o `HEAD`:**

```
 FAIL  DeclaredGap.test.tsx > NEGATIVO: sem escolher forma, a lacuna vem SEM moldura
AssertionError: expected 'border: 1px dashed var(--mantine-colo…' not to match /border|outline/
+ Received:
"border: 1px dashed var(--mantine-color-default-border); border-radius: var(--mantine-radius-sm); padding: 10px 12px;"
```

**2.4 — a varredura da fonte, contra o `HEAD`. Ela NOMEIA a linha:**

```
 FAIL  DeclaredGap.test.tsx > NEGATIVO: a FONTE do componente não declara contorno nenhum
AssertionError: expected [ …(2) ] to deeply equal []
+ [
+   "DeclaredGap.tsx:108 → border: '1px dashed var(--mantine-color-default-border)',",
+   "DeclaredGap.tsx:109 → borderRadius: 'var(--mantine-radius-sm)',",
+ ]
```

**2.6 — o guarda da #89, em DUAS pernas, porque ele tem duas.**

Perna 1, o `tsc`, com `'rejection-reason'` posto **só na união**:

```
$ npx tsc -b
src/features/insights/utils/caveatLabels.test.ts:100:7 - error TS2741:
Property '"rejection-reason"' is missing in type '{ 'task-duration': "render"; … }'
but required in type 'Record<CaveatPlacement, "render" | "nao-render">'.
Found 1 error.
```

Perna 2, em tempo de execução, depois de classificá-lo como posição de render:

```
 FAIL  caveatLabels.test.ts > toda posição de render tem pelo menos um código apontando para ela
AssertionError: expected [ 'rejection-reason' ] to deeply equal []
+ [ "rejection-reason" ]
```

**Desfeito**: `git diff src/features/insights/utils/caveatLabels.ts` vazio, e os 24
casos do arquivo verdes.

**E a perna 1 é a que importa para a classe:** ela é o que a #89 dizia não ter — a
união deixa de depender de alguém reler o arquivo. O `tsconfig.vitest.json` entra no
`tsc -b`, então o `npm run build` a executa.

## 3. A remoção

- [x] 3.1 `DeclaredGap.tsx`: tirar a prop `variant`, o branch de `block` e o atributo
      `data-gap-variant`. **Os valores do caminho `inline` viram constantes** —
      `fw={400}` e `c="dimmed"` —, e é aqui que o erro mora: colapsar o ternário pelo
      lado errado troca o peso, e jsdom não enxerga peso (`apps/frontend`)
- [x] 3.2 `InsightsKpiGrid.tsx:93` e `AgentModelsCard.tsx:149`: tirar
      `variant="inline"` (`apps/frontend`)
- [x] 3.3 Rodar `tsc` e conferir que **nenhum sítio** passa `variant` — é o guarda 5
      da D6, e ele só existe depois de 3.1. **Colar a saída** (`apps/frontend`)
- [x] 3.4 Adaptar os três guardas de `data-gap-variant` para afirmarem **a ausência da
      moldura** (`DeclaredGap.test.tsx:87`, `InsightsKpiGrid.test.tsx:68`,
      `AgentModelsCard.test.tsx:132`) (`apps/frontend`)
- [x] 3.5 **Remover** `DeclaredGap.test.tsx:97` — *"a variante block mantém a moldura
      tracejada do quadro 6"*. **É a categoria "removidos"**, e ela é contada à parte
      na medição (`apps/frontend`)
- [x] 3.6 Corrigir **com a causa** (convenção 9) os blocos de comentário que a change
      torna falsos: o bloco `VARIANTE` (`:47-63`), **o terceiro item da lista de
      quatro mecanismos de contenção do risco do qualificador** (`:40` — a lista passa
      a três, e o texto precisa dizer por que as três bastam) e o comentário da prop
      (`:70`) (`apps/frontend`)

## 4. A régua da fiação

- [x] 4.1 Com tudo verde, **tirar `<DeclaredGap>` de `InsightsKpiGrid.tsx`** e rodar a
      suíte de `insights`. **Colar o resultado** (`apps/frontend`)
- [x] 4.2 O mesmo em `AgentModelsCard.tsx` (`apps/frontend`)
- [x] 4.3 Se **algum dos dois** passar com a lacuna removida, é achado: escrever o
      guarda que falta, na camada que a mutação atravessou (`apps/frontend`)

### O resultado da régua da fiação — **não há vácuo**, e o guarda extra NÃO foi preciso

Com os guardas verdes, `<DeclaredGap>` foi trocado por `<></>` em cada consumidor, um
de cada vez, e a suíte inteira de `insights` (460 casos) rodada.

| mutação | reprovaram | onde |
|---|---|---|
| `InsightsKpiGrid.tsx` sem a lacuna | **5 de 460** | os cinco em `InsightsKpiGrid.test.tsx` |
| `AgentModelsCard.tsx` sem a lacuna | **4 de 460** | os quatro em `AgentModelsCard.test.tsx` |

**A projeção da D6 acertou, e a razão dela também.** A change anterior precisou de um
nono guarda porque a **página** garantia a prop e nunca afirmava a célula renderizada —
havia uma camada de fiação entre o dado e a tela. **Aqui essa camada não existe:** a
lacuna é JSX estático dentro do próprio componente, então o guarda de componente É o
guarda de fiação. Nenhum guarda novo foi escrito, e isso é resultado medido, não
omissão.

**O que vale registrar da régua:** ela não disse "está tudo bem", disse **onde** as
reprovações caem. As nove estão todas no arquivo do próprio consumidor e **nenhuma** em
`SystemInsightsPage.test.tsx` — o que é correto aqui e teria sido o sintoma na change
anterior. A leitura da régua é a distribuição, não a contagem.

### E as cinco trocas de guarda foram REVERIFICADAS contra a moldura reposta

Guarda negativo que troca de forma sem ser reverificado é guarda perdido, e a série já
tem cinco casos disso. Com o contorno reposto de propósito no componente **já
simplificado**, as cinco reprovam:

```
 ❯ DeclaredGap.test.tsx (8 tests | 3 failed)
     × a lacuna tem o PESO DE SUBTÍTULO, e continua marcada como lacuna
     × NEGATIVO: sem escolher forma, a lacuna vem SEM moldura
     × NEGATIVO: a FONTE do componente não declara contorno nenhum
 ❯ AgentModelsCard.test.tsx (15 tests | 1 failed)
     × a lacuna substitui um SUBTÍTULO, e não ganha contorno de coluna
 ❯ InsightsKpiGrid.test.tsx (19 tests | 1 failed)
     × a lacuna de turno × compactação está presente, como SUBTÍTULO
 Test Files  3 failed (3)
      Tests  5 failed | 37 passed (42)
```

**As três trocadas ficaram mais fortes, e isso é verificável:** a asserção antiga era
`not.toContain('dashed')` — um glifo de estilo —, e a nova é `not.toMatch(/border|outline/)`.
A de `AgentModelsCard` nem estilo afirmava: só o nome da variante. **Ela não teria
reprovado** contra um contorno reposto por outro caminho; agora reprova.

### E o `tsc` pegou o sítio que faltava, que é o guarda 5 em ação

```
$ npx tsc -b                    # logo depois de tirar a prop do componente
src/features/insights/components/DeclaredGap.test.tsx:76:11 - error TS2322:
Property 'variant' does not exist on type 'IntrinsicAttributes & DeclaredGapProps'.
Found 1 error.
```

**Era o único sítio restante no repositório**, e o compilador o apontou sem varredura
nenhuma — que é exatamente o que a D1 argumentou que a remoção compraria e a inversão
de padrão não compraria.

## 5. A conferência manual — e o archive espera por ela

Convenção 14: jsdom não enxerga peso, cor nem layout, e esta change colapsa dois
ternários de peso. **A conferência é quadro a quadro, nas duas telas e nos dois
esquemas de cor.**

- [x] 5.1 Subir o app e conferir a lacuna do KPI *"Chamadas ao provedor"* na página do
      sistema — ela tem de sair **idêntica** ao `HEAD`: uma linha, peso de subtítulo,
      esmaecida, sem moldura (`apps/frontend`)
- [x] 5.2 Conferir a lacuna *"Separação entre turno e compactação"* no card de modelos
      da aba do agente, na mesma linha da nota de configuração (`apps/frontend`)
- [x] 5.3 Repetir as duas no outro esquema de cor (`apps/frontend`)
- [x] 5.4 Comparar lado a lado com a captura do `HEAD`, não de memória (`apps/frontend`)

**CONFERIDAS PELO DONO em 01/10/2026**, com o app de pé (`apps/api` em `:5017` com
`TZ=America/Sao_Paulo`, Vite em `:5173`; `apps/inbox` fora, que os insights não usam).
As duas lacunas vistas nas duas telas e nos dois esquemas de cor: *"turno e
compactação — não disponível"* no KPI *Chamadas ao provedor* da página do sistema, e
*"Separação entre turno e compactação — não devolvida por esta rota"* na mesma linha da
nota de configuração, no card de modelos da aba do agente. **Sem moldura, no peso dos
subtítulos vizinhos** — a régua de comparação na própria linha é *"11 de origem
externa"* e *"média 10,7 s"*. **A condição que faltava para o archive está satisfeita.**

**Dois registros da conferência, nenhum deles desta change:**

- o card *"Tokens de embedding"* aparece **sem número** nos dois esquemas. O diff de
  produção dos dois consumidores é **uma linha removida em cada**, e o card lê
  `tokens.embeddingInputTokens` exatamente como lia (`InsightsKpiGrid.tsx:162-174`,
  intocado). **Não é regressão desta change**; se for defeito, é achado próprio;
- o **botão de tema não respondeu a clique sintético** — três tentativas, por
  coordenada e por referência —, e só funcionou chamando `.click()` por JavaScript.
  Pode ser limitação da automação; vale conferir no clique humano.

**O que já está fechado, e reduz o que a conferência tem de procurar:** o risco nomeado
no `design.md` era *colapsar os dois ternários de peso pelo lado errado*, e ele foi
medido de forma determinística — a árvore renderizada da lacuna, antes e depois,
comparada caractere a caractere:

```
ANTES (HEAD, variant="inline" — o que os dois sítios vivos produziam):
<div class="" data-testid="x" data-declared-gap="true" data-gap-variant="inline"><p
style="--text-fz: var(--mantine-font-size-xs); --text-lh: var(--mantine-line-height-xs);
color: var(--mantine-color-dimmed); font-weight: 400;" …>turno e compactação — <span
style="color: var(--mantine-color-dimmed);" …>não disponível</span></p></div>

DEPOIS:
<div class="" data-testid="x" data-declared-gap="true"><p
style="--text-fz: var(--mantine-font-size-xs); --text-lh: var(--mantine-line-height-xs);
color: var(--mantine-color-dimmed); font-weight: 400;" …>turno e compactação — <span
style="color: var(--mantine-color-dimmed);" …>não disponível</span></p></div>

$ diff antes depois
< <div class="" data-testid="x" data-declared-gap="true" data-gap-variant="inline">
---
> <div class="" data-testid="x" data-declared-gap="true">
```

**A ÚNICA diferença é o atributo que saiu de propósito.** `font-weight: 400`,
`color: var(--mantine-color-dimmed)` e os dois tokens de tamanho são idênticos — os
ternários foram colapsados pelo lado certo.

**Isto NÃO substitui a conferência** (a convenção 14 é explícita: jsdom não enxerga cor,
contraste nem layout, e a suíte inteira já passou verde com painel ilegível três vezes).
O que ele faz é fechar o risco **nomeado**, para que a conferência procure o que só o
olho pega. O arranjo foi um arquivo de teste descartável, rodado e **apagado**.

## 6. A suíte, e a medição da convenção 18

- [x] 6.1 Rodar a suíte inteira de `apps/frontend` em árvore limpa, com `podman ps` e
      `load average` declarados. **Abortar em vez de registrar número inválido**
      (`apps/frontend`)
- [x] 6.2 Medir **por categoria** — novos, adaptados, **removidos**, reforçados, de
      fiação, negativas novas — e comparar **categoria a categoria**, nunca só o total.
      A vigésima terceira acertou o total com as categorias erradas (raiz)
- [x] 6.3 Medir as **linhas adicionadas E as removidas**, separadas, e a proporção
      comentário : lógica da produção. **A hipótese da D9 é ~6:1**, e ela é
      falsificável: se vier abaixo de 4,4:1, a régua da remoção está errada e isso vai
      escrito (raiz)
- [x] 6.4 **Não corrigir a projeção depois de medir** — corrigi-la apagaria a medição
      (raiz)

### A medição — vigésima quinta. **A hipótese SE CONFIRMA, e a projeção erra o tamanho.**

**Suíte: 1413 / 118, zero falhas, 117 s**, contra a baseline de **1411 / 118** medida
duas horas antes em árvore limpa. Regime: `load average` **13,98** em 12 núcleos no
início, os dois contêineres de desenvolvimento no `podman ps`, **zero** `Unhandled
Error` de pool.

#### Casos — e o total E as categorias acertaram

| categoria | projetado | medido | erro |
|---|---|---|---|
| novos | 3 | **3** | **exato** |
| adaptados | 3 | **3** | **exato** |
| **removidos** | **1** | **1** | **exato** |
| reforçados | 0 | **0** | exato |
| de fiação | 0 | **0** | exato |
| **negativas novas** | 2 | **3** | **+50%** |
| **saldo** | **+2** | **+2** | **exato** |
| arquivos de teste | 118 | **118** | exato |
| arquivos tocados | 7 modificados, 0 criados | **7 / 0** | **exato** |

**A única categoria que errou é a das negativas, e a causa é reusável.** Projetei 2
contando as negativas **de tela** — os dois guardas da D6 que afirmam a ausência do
contorno. A terceira é o guarda da #89: `expect(orfas).toEqual([])` **é uma negativa**,
só que **sobre o código, não sobre a tela**. A régua da 5a-4 dizia *projetar negativas
lendo os cenários da delta*; esta acrescenta que **nem toda negativa tem cenário de
spec** — uma change que entrega guarda de forma produz negativas que a delta não
descreve, porque não é a tela que elas negam.

#### Linhas — erro de **+47%**, e a mistura é que acertou

Critério: linhas **adicionadas não vazias** de `git diff -U0`, comentário = linha que
começa com `//`, `*` ou `/*`.

| | projetado | medido | erro |
|---|---|---|---|
| criados | 0 arquivos / 0 linhas | **0 / 0** | exato |
| **produção adicionada** | ~35 | **48** | +37% |
| **teste adicionado** | ~80 | **114** | +43% |
| **total adicionado** (não vazio) | ~115 | **162** | **+41%** |
| total adicionado (com vazias) | — | **169** | — |
| **removidas** | ~55 | **77** | +40% |

#### O VEREDITO DA HIPÓTESE: **confirmada**

A D9 afirmou que **numa change de remoção a régua do comentário não inverte, ela se
agrava**, por dois efeitos que somam — o numerador cresce porque o mecanismo sai do
arquivo e o texto passa a carregá-lo inteiro; o denominador encolhe porque se tira
lógica. **Projeção: ~6:1. Falsificação declarada: abaixo de 4,4:1 a régua está errada.**

| medição | change | tipo | comentário : lógica na produção |
|---|---|---|---|
| 23ª | `insights-sistema-motivo-da-recusa` | consumo + correção | 1,98 : 1 |
| 24ª | `consumo-por-agente-falhas` | correção | 4,4 : 1 |
| **25ª** | **`declared-gap-variante-morta`** | **remoção** | **5,0 : 1** (40 comentário : 8 lógica) |

**5,0:1 está acima de 4,4:1, então a hipótese passa** — e passa pela margem certa, não
por acaso: a produção entregou **8 linhas de lógica** contra **54 removidas**. O
denominador encolheu como previsto, e `DeclaredGap.tsx` fechou **menor** (116 → 110
linhas) com **mais comentário** do que tinha.

**A projeção do NÚMERO errou 20% para cima** (6:1 contra 5:1), e isso **não enfraquece
a hipótese**: ela era sobre a **direção e o piso**, e os dois acertaram. É a mesma
separação que a 24ª não fez — ali o erro de volume foi lido como erro de régua.

#### O que a projeção NÃO soube prever, e é o que vale para a próxima

**O volume errou +41%, e a parcela inteira está no TESTE** (114 contra ~80), não na
produção. Duas causas, as duas por leitura:

- **guarda com arranjo próprio custa o dobro do guarda de asserção.** A varredura
  estática de fonte (ler o arquivo, filtrar linha, montar a mensagem) e o `Record`
  exaustivo da #89 **não são `expect` de uma linha** — são 17 e 23 linhas de lógica.
  Projetei-os como "mais dois casos";
- **o comentário do teste também se projeta.** Dos 114, **69 são comentário** (1,53:1).
  A régua *"o comentário é o produto"* foi aplicada à produção e **não ao teste** — e
  num guarda cuja razão de existir é uma classe de defeito, o teste carrega o registro
  tanto quanto o componente.

**Régua para a próxima: projetar o comentário dos DOIS lados, e separar "caso de
asserção" de "caso com arranjo" no custo unitário.**

**A projeção não foi corrigida.**

## 7. A documentação, que fecha ANTES do archive

**Nenhum PR de cauda, nenhum commit de cauda** (convenção 24).

- [x] 7.1 `01-ARQUITETURA_E_CONVENCOES.md` — a convenção nova da **declaração viva sem
      consumidor**, com a tabela das duas ocorrências da D8, as **duas formas opostas
      de sobrevivência** (teste verde que mantém vivo × nenhum teste) e as **duas
      ferramentas** que a varrem (varredura de fonte para o textual, exaustividade do
      `tsc` para o tipo). É o conteúdo exclusivo da #89 (raiz)
- [x] 7.2 `01` — conferir se a convenção **18** precisa da vigésima quinta medição no
      corpo dela, e se a **22** ganha a sétima ocorrência com o critério de varredura
      que nasceu estreito (D5) (raiz)
- [x] 7.3 `02-HISTORICO_E_STATUS.md` — a entrada da change. **Ela NÃO afirma estado de
      publicação**: nem "commitada", nem "não commitada". O `02` é registro do que foi
      decidido (raiz)
- [x] 7.4 `CHANGELOG.md` (raiz)
- [x] 7.5 Conferir se a change torna falso algo em `docs/` — **varrido pela forma**
      (`DeclaredGap|lacuna declarada|variant|moldura|tracejad|quadro 6`) em `docs/`,
      `README.md`, `CHANGELOG.md` e `01`. **`docs/` não tem ocorrência nenhuma.** As do
      `02` são **registro histórico de rodadas**, que descreve o que era verdade
      naquele dia e não é tornado falso — a única afirmação de **estado corrente** era
      o item aberto (a) da entrada da #75, corrigido com a causa. As duas do
      `CHANGELOG` sobre "quadro tracejado" são de **vínculo de base de conhecimento**,
      outro componente (raiz)

## 8. O fechamento das issues, e o que fica pendente do dono

- [x] 8.1 Rodar `openspec validate --all` e conferir verde (raiz)
- [x] 8.2 Sincronizado no archive, depois da conferência do dono. A aplicação em
      `openspec/specs/system-insights-ui/spec.md` é **estritamente aditiva — 23 linhas
      inseridas, 0 removidas**: dois parágrafos normativos e dois cenários (de 5 para
      7 no requisito), com os cinco anteriores idênticos. `openspec validate --all`
      verde, e `--specs --strict` também (raiz)
- [x] 8.3 **Mover o conteúdo exclusivo da #89 antes de ela fechar** — a tabela das duas
      ocorrências e o ponto da união não varrível já estão na D8/D5 e vão para o `01`
      na 7.1. É a ordem que a #80 ensinou (raiz)
- [x] 8.4 **Autorizada pelo dono e aberta: #94** — *"Declaração cujo consumidor é uma
      CÓPIA: `theme.test.ts` redeclara `HEAT_STEPS`, e o guarda cobre a cópia"*,
      rotulada `tipo: débito técnico` + `app: frontend`. O corpo registra **a classe**
      com as **três** formas, não só o caso (raiz)
- [ ] 8.5 **Pendente do dono:** o archive, o commit, o push e o PR. O PR carrega
      `Closes #83, Closes #89`, e o archive **precede** o push (convenção 24) (raiz)
