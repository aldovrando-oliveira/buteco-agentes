# Tarefas — exclusão de base de conhecimento no painel (#136)

**Cada tarefa indica o app.** O código roda em `apps/frontend` (grupos 1 a 5) e em
`apps/api` (grupo 6); a documentação (grupo 8) roda na raiz e não pertence a app
nenhum. Nada em `apps/connectors`, `apps/workers`, `apps/inbox` nem `libs/`. Se a
tela precisar de algo que a rota da #108 não oferece, PARAR e reportar.

**Worktree:** `/Users/aldovrando/Projetos/buteco-agents-136`, branch
`feat/136-exclusao-base-painel`, criada sem upstream de `origin/main` (`043cf69`)
em 05/10/2026. Confirmar `pwd` e `git branch --show-current` antes de cada grupo.
Nenhum comando git no diretório principal nem em outro worktree; nunca
`checkout`, `switch`, `stash`, `reset`, `clean`, `pull`, `merge` nem `rebase`.

**Trabalho em paralelo:** a #119 corre em `/Users/aldovrando/Projetos/buteco-agents-119`
e edita o `02-HISTORICO_E_STATUS.md`, o `CHANGELOG.md` e `docs/`. Edições curtas e
localizadas nesses arquivos; nada do compose, do nginx nem do `deployment.md`.
Conferir o `load average` antes de classificar qualquer falha de teste, e com a
máquina carregada esperar e refazer. Portas só na faixa **59100–59199**, cada uma
conferida com `lsof -i :<porta>`; nunca derrubar processo que esta change não
iniciou.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`) `npm ci` neste worktree e a baseline sem nenhuma edição:
      `npm run lint`, `npm run format:check`, `npm run test`, `npm run build`, com o
      `load average` anotado. Registrar aqui cada resultado e, no `format:check`, a
      lista de arquivos que já reprovam — só esses contam como pré-existentes.
      Feito em 05/10/2026, em `043cf69` sem nenhuma edição de código, depois de
      `npm ci` neste worktree.
      - `npm run lint`: **verde** (saída 0). Load 4,01.
      - `npm run format:check`: **reprova** (saída 1) em **52 arquivos**, a lista
        abaixo. Nenhum é arquivo que esta change edita.
      - `npm run test`: **verde** — 130 arquivos, **1685/1685**, 172,6 s. Load 5,6
        no início e **175,8** no fim (a suíte e o stack da #119 juntos).
      - `npm run build`: **verde** (saída 0).

```
src/features/agents/pages/AgentDetailPage.test.tsx
src/features/insights/api/insightsApi.test.ts
src/features/insights/api/insightsApi.ts
src/features/insights/api/useAgentInsights.test.tsx
src/features/insights/components/AgentConsumptionCard.test.tsx
src/features/insights/components/AgentConsumptionCard.tsx
src/features/insights/components/AgentDelegationCard.test.tsx
src/features/insights/components/AgentDelegationCard.tsx
src/features/insights/components/AgentFailuresCard.test.tsx
src/features/insights/components/AgentFailuresCard.tsx
src/features/insights/components/AgentInsightsTab.test.tsx
src/features/insights/components/AgentInsightsTab.tsx
src/features/insights/components/AgentKpiGrid.test.tsx
src/features/insights/components/AgentKpiGrid.tsx
src/features/insights/components/AgentModelsCard.test.tsx
src/features/insights/components/ConversationModelsCard.test.tsx
src/features/insights/components/ConversationModelsCard.tsx
src/features/insights/components/DailyTasksCard.test.tsx
src/features/insights/components/DailyTasksCard.tsx
src/features/insights/components/DeclaredGap.test.tsx
src/features/insights/components/FailureReasonsCard.test.tsx
src/features/insights/components/FailureReasonsCard.tsx
src/features/insights/components/FailuresCard.test.tsx
src/features/insights/components/InsightsKpiGrid.test.tsx
src/features/insights/components/MetricValue.test.tsx
src/features/insights/components/NonTerminalBanner.test.tsx
src/features/insights/components/PartialMeasurementNotice.test.tsx
src/features/insights/components/PeriodHeatmapCard.tsx
src/features/insights/components/PeriodPicker.tsx
src/features/insights/components/ProviderConsumptionCard.tsx
src/features/insights/components/TaskDurationCard.test.tsx
src/features/insights/components/TaskDurationCard.tsx
src/features/insights/components/TimeBreakdownCard.test.tsx
src/features/insights/components/TimeBreakdownCard.tsx
src/features/insights/components/WeekdayActivityCard.test.tsx
src/features/insights/components/WeekdayActivityCard.tsx
src/features/insights/pages/SystemInsightsPage.test.tsx
src/features/insights/pages/SystemInsightsPage.tsx
src/features/insights/test/agentInsightsFixture.ts
src/features/insights/test/systemInsightsFixture.ts
src/features/insights/utils/caveatLabels.test.ts
src/features/insights/utils/caveatLabels.ts
src/features/insights/utils/delegationOutcomeLabels.test.ts
src/features/insights/utils/delegationOutcomeLabels.ts
src/features/insights/utils/delegationRows.test.ts
src/features/insights/utils/failurePhaseLabels.test.ts
src/features/insights/utils/insightsWindow.test.ts
src/features/insights/utils/insightsWindow.ts
src/features/insights/utils/measuredDays.test.ts
src/features/knowledge-bases/pages/KnowledgeBaseEditPage.test.tsx
src/features/knowledge-bases/pages/KnowledgeBaseEditPage.tsx
src/features/mcp-servers/utils/agentUsage.ts
```

- [x] 1.2 (`apps/api`) Baseline de `dotnet test apps/api/Api.sln` (Podman e
      `DOCKER_HOST` conforme `docs/development.md`, `TZ=America/Sao_Paulo`), com o
      `load average`. Registrar a contagem medida, nunca a de memória.
      Feito em 05-06/10/2026, em `043cf69` sem edição, com `DOCKER_HOST` do Podman,
      `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e
      `TZ=America/Sao_Paulo`: **verde, 756/756**, 5 min 29 s. Load **150,4** no
      início (a suíte do frontend acabara de terminar) e 22,2 no fim.
- [x] 1.3 (`apps/frontend`) Conferir no pacote instalado (`node_modules/@mantine/core`)
      que o `Button` aceita `data-disabled` mantendo o foco e que o `Tooltip` abre no
      foco (D1, convenção 6). Registrar aqui o arquivo e a linha lidos, e, se não
      suportar, usar o `span` focável da D1.
      Lido em `node_modules/@mantine/core` **9.4.2**:
      - `esm/components/Button/Button.mjs:52` desestrutura `"data-disabled"`;
        `:72` passa ao elemento só `disabled: disabled || loading` — o
        `data-disabled` **não** vira `disabled` nativo, então o botão continua
        focável; `:74` põe o `data-disabled` no `mod`, que dá o estilo.
      - `styles/Button.css:63-69`: `[data-disabled]` muda cursor, cor e fundo, **sem**
        `pointer-events: none` (o único da regra, `:73`, é do `::before`), então o
        `mouseenter` chega ao `Tooltip`.
      - `esm/components/Tooltip/Tooltip.mjs:40-44`: o padrão é
        `events: { hover: true, focus: false, touch: false }` — **o `Tooltip` não
        abre no foco por padrão**; `esm/components/Tooltip/use-tooltip.mjs:61` liga
        o foco por `events.focus`. Por isso o botão passa
        `events={{ hover: true, focus: true, touch: false }}`.
      **Resultado:** caminho principal da D1 (`data-disabled` + `aria-disabled`, clique
      anulado), **sem** a alternativa do `span`.

## 2. Cliente e mutação

- [x] 2.1 (`apps/frontend`) `deleteKnowledgeBase(id)` em `knowledgeBasesApi.ts`,
      `DELETE /knowledge-bases/{id}` pelo `request` da feature; reescrever o
      comentário "Não há DELETE" (convenção 13). Teste: método, caminho e `204` sem
      corpo; `409`, `404` e `503` chegam como `ApiError` com o status e o `code`.
- [x] 2.2 (`apps/frontend`) `useDeleteKnowledgeBaseMutation` em `useKnowledgeBases.ts`,
      **sem** mexer no cache (D3.1: quem mexe é a página, na ordem certa). Teste:
      chama a rota e propaga o erro.

## 3. Textos

- [x] 3.1 (`apps/frontend`) `utils/deleteKnowledgeBaseMessages.ts`: texto por resposta
      da tabela da D3 (`409` `knowledge-base-active`, `404`, `503`, outro status,
      rede) e se a resposta permite repetir. Teste por linha, mais: nenhum texto
      contém o `title` ou o `detail` recebidos; `409` com `code` desconhecido cai no
      texto de outro status, não no de base ativa.
- [x] 3.2 (`apps/frontend`) Texto de `folder-in-use` em `connectorErrors.ts` (D5),
      com a frase de base sem nome, e o comentário reescrito. Trocar a asserção
      negativa de `connectorErrors.test.ts:99-113` pela afirmação do nome, de
      "inativa", "desative", "exclua" e "não pode ser desfeita"; novo caso sem
      `knowledgeBaseName`, que não contém "sem nome".
- [x] 3.3 (`apps/frontend`) Ramo `folder-in-use` de
      `KnowledgeBaseCreatePage.sync.test.tsx:548-551`: a mesma troca.
- [x] 3.4 (`apps/frontend`) Só os comentários de `syncStateMessages.ts:18-22` e
      `syncStateMessages.test.ts:27-29`, com o motivo novo da D6. Nenhum texto muda;
      a varredura continua passando.

## 4. Confirmação e botão

- [x] 4.1 (`apps/frontend`) `components/KnowledgeBaseDeleteModal.tsx` (D2, D3), com
      dados e callbacks por prop (convenção 7): nome, contagem de documentos ou
      desconhecida, agentes ou desconhecidos, origem sincronizada, estado da
      requisição e o texto de erro. O comentário da frase dos agentes cita
      `KnowledgeToolSetResolver.cs:87` (`apps/workers`) e o gatilho (convenção 13).
- [x] 4.2 (`apps/frontend`) `KnowledgeBaseDeleteModal.test.tsx`: cada frase por
      estado de dado (N, 1, zero, desconhecido; agentes, nenhum, desconhecido;
      sincronizada e manual), com as negativas da seção "Testes de aceite" do design; `Cancelar`
      e `Esc` chamam só o fechar; durante a requisição o modal não fecha.
      **Visto reprovando depois, a pedido do mantenedor** (o componente foi escrito
      antes de o teste rodar), em 06/10/2026, load 3,1:
      1. Corpo do componente trocado por `return null`: **13 de 13** reprovaram.
      2. Só as duas frases de desconhecido trocadas por "os seus 0 documentos" e
         "Nenhum agente está vinculado a esta base.": reprovou **só** "documentos e
         agentes desconhecidos não viram zero nem \"nenhum\"" (12 passaram), na
         asserção positiva `toHaveTextContent` (a primeira do teste). Para provar as
         negativas, as duas positivas foram retiradas temporariamente do teste e a
         mutação repetida: com as duas frases trocadas, reprovou em
         `not.toMatch(/\d/)`; com só a de agentes trocada, reprovou em
         `not.toHaveTextContent(/Nenhum agente/i)`. As negativas provam a ausência
         sozinhas; **nenhum teste precisou de correção**.
      3. Componente e teste restaurados das cópias (`cmp` sem diferença; `grep` por
         `0 documentos` e `return null;` no componente: 0); arquivo verde, 13/13.
- [x] 4.3 (`apps/frontend`) Botão `Excluir` no cabeçalho de
      `KnowledgeBaseDetailPage.tsx` (D1), a mutação e o tratamento das respostas
      (D3), com a ordem do cache da D3.1 e a invalidação de `['agents']`.
- [x] 4.4 (`apps/frontend`) `pages/KnowledgeBaseDetailPage.delete.test.tsx`, os testes
      de aceite da página: base ativa (`aria-disabled`, explicação no foco, clique sem
      confirmação e `deleteKnowledgeBase` não chamada); base inativa com nome,
      agentes e "3 documentos"; cancelar sem chamada; `204` na listagem com a
      notificação e sem a base; `409`, `404`, `503` e rede com o seu texto, sem
      navegar, sem `title`/`detail` do servidor; `409` relê a base; `503` mantém a
      exclusão disponível.
- [x] 4.5 (`apps/frontend`) **Guarda (convenção 15):** trocar `data.isActive` pela
      constante `false` na condição do botão, rodar o teste da base ativa, ver
      reprovar, registrar aqui a saída, e restaurar. Conferir que reprova **no
      teste da página**, que é o componente que a condição toca.
      Feito em 06/10/2026, load 6,8: as **4** ocorrências de `data.isActive` dentro do
      bloco do `Tooltip`/`Button` (`disabled` do Tooltip, `data-disabled`,
      `aria-disabled` e o desvio do `onClick`) trocadas por `false`. Reprovaram os
      dois testes de `KnowledgeBaseDetailPage.delete.test.tsx`, ambos da página:
      - "o botão está indisponível, o clique não abre a confirmação e nada é
        chamado": `expect(element).toHaveAttribute("aria-disabled", "true")`;
      - "o foco no botão mostra que é preciso desativar antes":
        `TestingLibraryElementError: Unable to find role="tooltip"`.
      Arquivo restaurado da cópia; `grep` por `false || undefined` e
      `disabled={!false}` na página: **0**. Suíte do arquivo de novo: 11/11.
- [x] 4.6 (`apps/frontend`) Teste no card de Origem de base `Synced` **inativa** com
      `access-denied`: o card não contém `exclu`, `remov` nem `apag` (cenário novo
      de `knowledge-base-sync-state-ui`).
      Escrito na página (`KnowledgeBaseDetailPage.delete.test.tsx`, "alerta de falha
      em base sincronizada inativa"), e não no teste do card: o card não recebe
      `isActive`, e o cenário é justamente o do card ao lado do botão disponível.

## 5. Verificação do frontend

- [x] 5.1 (`apps/frontend`) `npm run lint`, `npm run format:check` (só os arquivos da
      baseline reprovam), `npm run test` e `npm run build`, com o `load average`.
      Registrar as contagens medidas.
      Feito em 06/10/2026, depois de `prettier --write` só nos arquivos desta change:
      - `npm run lint`: **verde**. Load 7,4.
      - `npm run format:check`: saída 1 com **a mesma lista de 52 arquivos da
        baseline** (`diff` vazio); nenhum arquivo desta change reprova.
      - `npm run test`: **verde — 133 arquivos, 1724/1724**, 180,2 s (baseline: 130 e
        1685). **+3 arquivos e +39 testes**, todos desta change: 4 do cliente, 2 da
        mutação, 7 dos textos de resposta, 1 de `folder-in-use` sem nome, 13 da
        confirmação e 12 da página. Load 8,6 no início e 107,5 no fim.
      - `npm run build`: **verde**, só o aviso de chunk acima de 500 kB, como na
        baseline.

## 6. `apps/api`: a mensagem de pasta em uso

- [x] 6.1 (`apps/api`) Novo `detail` de `FolderInUse` em `KnowledgeBaseEndpoints.cs`
      (D4) e o comentário apontando esta change. `title`, `code`, status e extensões
      não mudam.
- [x] 6.2 (`apps/api`) Em `KnowledgeBaseCatalogTests.SyncedCreation.cs`, tirar
      `DeletionWords` e `AssertNoDeletionAdvice`; o teste
      `CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt` afirma no
      `detail` o nome, "inativa", a alternativa de "outra pasta", "desative", "exclua"
      e "não pode ser desfeita".
      **Segundo teste, autorizado pelo mantenedor em 06/10/2026:**
      `CreateSynced_TwoConcurrentCreatesOfTheSameFolder_OneCreatesTheOtherGets409`
      (`:381`) também usava `AssertNoDeletionAdvice`, porque o `409` da corrida sai
      pelo mesmo `FolderInUse`, pelo caminho do índice único. A linha passou a
      `AssertBothWaysOut` sobre o `detail` do `409` da corrida; o resto do teste (um
      `201`, um `409` com o nome da vencedora, nenhum `500`, uma base no banco, o
      evento `1024` uma vez por iteração) não mudou. `DeletionWords` e
      `AssertNoDeletionAdvice` saíram; `grep` nos `apps/`: nenhum uso.
      Antes da 6.1, os 3 casos (os dois do `Theory` e o da corrida) reprovaram com
      `Assert.Contains() Failure: Sub-string not found / Not found: "outra pasta"`.
- [x] 6.3 (`apps/api`) **Guarda (convenção 15):** tirar a orientação do `detail`, ver
      o teste reprovar, registrar a saída, restaurar.
      Feito em 06/10/2026, load 8,2: a frase "Escolha outra pasta, ou desative e
      exclua…" tirada do `detail`. Reprovaram os **3** casos:
      `CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt(existingIsActive:
      True)` e `(False)`, e `CreateSynced_TwoConcurrentCreatesOfTheSameFolder_OneCreatesTheOtherGets409`,
      todos com `Not found: "outra pasta"`. Arquivo restaurado da cópia (`cmp` sem
      diferença; "Escolha outra pasta" presente 1 vez).
- [x] 6.4 (`apps/api`) `dotnet test apps/api/Api.sln`, com o `load average`. Registrar
      a contagem medida e comparar com a baseline 1.2.
      Feito em 06/10/2026, com o load de 1 minuto em **6,72** na conferência e 6,34 no
      início: **verde, 756/756**, 1 min 50 s — a mesma contagem da baseline, porque a
      change alterou testes e não acrescentou nenhum no `apps/api`. Load 12,6 no fim.

## 7. Conferência visual (D7)

- [x] 7.1 (`apps/frontend`) Subir o Vite numa porta livre da faixa 59100–59199, com
      `VITE_API_BASE_URL` numa segunda porta da faixa sem processo escutando;
      registrar as duas aqui.
      Feito em 06/10/2026: Vite em **59101** (PIDs 34965, `npm exec`, e 34985, o
      `vite`), `VITE_API_BASE_URL=http://localhost:59102` e
      `VITE_CONNECTORS_BASE_URL=http://localhost:59104`, as duas **sem processo
      escutando**; Chrome headless com `--remote-debugging-port=59103` (PID 34966).
      As quatro conferidas livres com `lsof` antes.
- [x] 7.2 (`apps/frontend`) Chrome headless por CDP, respostas montadas pelo `Fetch`,
      claro e escuro, 1440 e ~1860 px, os estados da D7 e a régua (confirmação de
      exclusão de documento). Capturas e medidas em
      `openspec/changes/exclusao-base-painel/design/capturas/`. Iterar até uma
      rodada sem achado; cada achado registrado na D7 com o número medido.
      Feito em 06/10/2026: **12 estados × 2 esquemas × 2 larguras = 48 capturas por
      rodada**, roteiro CDP com `Fetch.fulfillRequest` (preflight `OPTIONS`
      atendido) e `Fetch.failRequest` para a falha de rede; o foco do `Tooltip` por
      Tab de verdade (`Input.dispatchKeyEvent`), porque o `.focus()` programático
      não abre o tooltip. **Duas rodadas**: a primeira com um achado (R1-1), a
      segunda sem achado. Capturas da rodada 2 e as medidas das duas
      (`medidas-r1.json`, `medidas-r2.json`) na pasta; resultado na D7.
- [x] 7.3 (`apps/frontend`) Encerrar só os processos que esta change iniciou.
      Encerrados os PIDs 34965, 34985 e 34966; `lsof` nas portas 59101 a 59104:
      todas livres.
- [x] 7.4 **Validação manual pelo mantenedor** (convenção 14), nos dois esquemas. O
      archive espera por ela.
      Feita em 11/10/2026 pelo mantenedor, com a pilha local deste worktree
      (`apps/api`, `apps/workers`, `apps/connectors` com a conta de serviço do Drive
      e o painel em 5173) e as bases "Validação 136 — manual" e "Validação 136 —
      sincronizada": aprovada.

## 8. Documentação (raiz do repositório)

Escrita antes de qualquer archive, mesmo que o archive seja pedido antes.

- [x] 8.1 `01-ARQUITETURA_E_CONVENCOES.md`: as duas linhas que dizem que a mensagem
      de pasta em uso espera a #136 (hoje `:131-132` e `:769-770`) passam a dizer que
      ela orienta desativar e excluir. Edição de frase, sem reescrever a seção.
- [x] 8.2 `docs/architecture.md`: uma frase em "Pasta em uso" (`:745`) com a
      orientação, e uma linha em "A exceção: exclusão de base" dizendo que o painel
      oferece a ação desde a #136.
- [x] 8.3 `openspec/specs/knowledge-base-sync-state-ui/spec.md`: a frase do `Purpose`
      sobre a exclusão ser escopo de outra change passa ao motivo da D6 (o delta não
      alcança o `Purpose`). Feito junto com o sync, antes do archive.
- [x] 8.4 `02-HISTORICO_E_STATUS.md`: entrada da change (o que mediu, as guardas, a
      conferência visual) e C1 em "Correções de protótipo". Seção acrescentada, sem
      reescrever trechos.
- [x] 8.5 `CHANGELOG.md`, em `[Unreleased]`: `Added` (exclusão de base no painel) e
      `Changed` (mensagem de `folder-in-use` no `apps/api` e no painel).
- [x] 8.6 `python3 scripts/check-docs.py` verde. (06/10/2026: "Integridade da
      documentação: OK".)
- [x] 8.7 `openspec validate exclusao-base-painel --strict` verde. (06/10/2026:
      "Change 'exclusao-base-painel' is valid".)
