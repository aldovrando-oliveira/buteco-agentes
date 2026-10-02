# Tarefas — cards da base acima das abas (#99)

**As tarefas de código e verificação rodam em `apps/frontend`.** Nenhuma toca
`apps/api`, `apps/workers`, `apps/inbox` nem `deploy/`. As do grupo 7 (e a
5.2) rodam na raiz do repositório e não pertencem a app nenhum.

**Branch:** `feat/99-detalhe-base-cards-acima-das-abas`, criada da `main`
atualizada em 02/10/2026 (`47c33b0`).

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`) Rodar a baseline num `git worktree` limpo da
      `main`: `npm run lint`, `npm run format:check`, `npm run test`,
      `npm run build`. Registrar aqui o resultado de cada um e, para o
      `format:check`, **a lista de arquivos que já reprovam** — só esses contam
      como pré-existentes. Conferir o load da máquina antes de medir a suíte
      (VM do Podman carregada derruba workers do Vitest).
      Feito em 02/10/2026, worktree destacado em `47c33b0` (`main`), com o
      `node_modules` do checkout principal por link simbólico; load 3,2 em 12
      núcleos com a VM do Podman ligada.
      - `npm run lint`: **verde** (saída 0).
      - `npm run format:check`: **reprova** (saída 1) em **52 arquivos**, a lista
        abaixo. Nenhum dos arquivos desta change está nela.
      - `npm run test`: **verde** — 118 arquivos, **1431/1431** testes, 108,76s.
      - `npm run build`: **verde** (só o aviso de tamanho de chunk de sempre).

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


## 2. Testes de aceite, escritos antes da implementação

- [x] 2.1 (`apps/frontend`) `KnowledgeBaseDetailPage.test.tsx`: com
      `?tab=diagnostico`, a seção de descrição e a seção de agentes são
      exibidas (reprova hoje).
- [x] 2.2 (`apps/frontend`) `KnowledgeBaseDetailPage.test.tsx`: com cada aba
      ativa, nem a descrição nem o card de agentes estão dentro do `tabpanel`;
      os dois vêm antes do `tablist` na ordem do documento, a descrição antes
      dos agentes (`compareDocumentPosition`). Afirmar também a negativa: o
      card de agentes **não** está dentro do painel de `Documentos`.
- [x] 2.3 (`apps/frontend`) `KnowledgeBaseDetailPage.test.tsx` e
      `KnowledgeBaseAgentsCard.test.tsx`: com `listAgents` pendente (card com
      `status` `pending` e sem `agents`), o estado de carregamento completo —
      indicação de carregamento **presente**; texto de indisponibilidade,
      estado vazio e contagem **ausentes**. Com `listAgents` rejeitado, nenhuma
      contagem. No card, com `agents` na mão e `status` `error`, a lista
      continua exibida (`design.md`, D5).
- [x] 2.4 (`apps/frontend`) `KnowledgeBaseAgentsCard.test.tsx`: chip com link
      para `/agents/{id}` e nome acessível igual ao nome do agente; sete
      agentes num único agrupamento de lista (sete `listitem`).
- [x] 2.5 (`apps/frontend`) `KnowledgeBaseAgentsCard.test.tsx`: reescrever
      "exibe o estado de cada agente" como "marca por texto só o agente
      inativo" — `Inativo` dentro do chip do inativo, ausente no do ativo, e
      nenhum `Ativo` no card (`design.md`, D2). É a única asserção existente
      que muda.
- [x] 2.6 (`apps/frontend`) `KnowledgeBaseAgentsCard.test.tsx`: contagem
      `7 agentes` com sete vinculados, `1 agente` com um; nenhuma contagem no
      estado vazio e no indisponível.
- [x] 2.7 (`apps/frontend`) `KnowledgeBaseAgentsCard.test.tsx`: estado vazio e
      estado indisponível com o texto de hoje — os testes existentes cobrem; não
      mudar asserção.
- [x] 2.8 (`apps/frontend`) Rodar os testes novos e ver reprovar pelo motivo
      certo antes da implementação.
      Feito: **10 reprovaram** antes da implementação, todos pelo motivo
      esperado — cards ausentes com `?tab=diagnostico`, `knowledge-base-agents-card`
      inexistente (o card ainda era o antigo, dentro do painel), sem `list`/`li`
      de chips, sem `agents-count`, sem `agents-loading`. **Quatro negativos já
      passavam** antes da implementação (sem contagem no vazio, sem contagem na
      falha no card e na página, lista mantida com `status` `error`): o código
      antigo não tinha contagem nem `status`, então eles só valem pelos guardas
      da 4.1, onde todos reprovaram. Os demais testes existentes de 2.7 seguiram
      verdes.

## 3. Implementação

- [x] 3.1 (`apps/frontend`) `KnowledgeBaseDetailPage.tsx`: mover
      `KnowledgeBaseDescriptionCard` e `KnowledgeBaseAgentsCard` para a `Stack`
      da página, entre `DetailHeader` e `<Tabs>`, nessa ordem; o painel de
      `Documentos` fica só com `KnowledgeDocumentsCard`. Barra, `keepMounted`,
      contador e modais intocados (`design.md`, D1).
- [x] 3.2 (`apps/frontend`) `KnowledgeBaseAgentsCard.tsx`: chips em lista
      (`ul`/`li`) que quebra na horizontal; link com o nome; `Inativo` dentro do
      chip e fora do link para o agente inativo; cores por variável que troca
      de esquema, nunca `gray[n]`/`dark[n]` fixos (`design.md`, D2 e D4).
- [x] 3.3 (`apps/frontend`) `KnowledgeBaseAgentsCard.tsx`: contagem no slot
      `action` do `SectionedCard`, só com lista carregada e não vazia, singular
      e plural (`design.md`, D3). Estados vazio e indisponível com o texto de
      hoje.
- [x] 3.4 (`apps/frontend`) Repassar o estado da consulta da página para o
      card (`status={agentsQuery.status}` ao lado de `agents`) e implementar o
      terceiro estado: `agents` definido → lista ou vazio; `pending` →
      carregamento no padrão de `KnowledgeDocumentsCard` ("Carregando
      agentes..."); `error` → texto de indisponibilidade de hoje (`design.md`,
      D5).
- [x] 3.5 (`apps/frontend`) Atualizar os comentários que descrevem a posição
      antiga (página, linhas 289-306 e 332-334) para a nova, e o comentário de
      `InventoryPage.tsx:126-128` que cita `KnowledgeBaseAgentsCard.tsx:17-27`;
      não deixar afirmação que deixou de ser verdade.

## 4. Guardas verificados contra o defeito real

- [x] 4.1 (`apps/frontend`) Reintroduzir de propósito cada defeito e ver o teste
      correspondente reprovar **no componente certo**: cards de volta para
      dentro do painel (2.1, 2.2); contagem exibida com `agents` indefinido
      (2.3, 2.6); `Ativo` de volta no chip do ativo (2.5); `Inativo` dentro do
      link (2.4); card voltando a tratar `agents` indefinido como falha,
      ignorando `status` (2.3). Desfazer e registrar aqui o resultado de cada
      um.
      Feito, cada defeito aplicado por script sobre o arquivo, rodado contra os
      dois arquivos de teste e desfeito (`git diff --stat` idêntico antes e
      depois):

      | guarda | defeito reintroduzido | reprovou |
      |---|---|---|
      | G1 | Descrição e Agentes de volta dentro do painel de `Documentos` (página) | página: "exibe descrição e agentes com a aba de diagnóstico ativa"; "com a aba Documentos ativa, os dois cards ficam antes da barra e fora do painel"; "com a aba Diagnóstico do índice ativa, …" (3) |
      | G2 | contagem no ramo `pending` | card: "durante o carregamento, indica carregamento e não afirma falha, ausência nem contagem"; página: "enquanto os agentes carregam, …" (2) |
      | G3 | contagem no ramo de falha | card: "não exibe contagem com o catálogo indisponível"; página: "com os agentes indisponíveis, não exibe contagem" (2) |
      | G4 | zero medido exibido como `0 agentes` | card: "não exibe contagem quando nenhum agente consulta a base" (1) |
      | G5 | `Ativo`/`Inativo` em todo chip | card: "marca por texto só o agente inativo" (1) |
      | G6 | `Inativo` dentro do link | card: "marca por texto só o agente inativo" (1) — e não o 2.4, porque é esse teste que busca o link pelo nome exato do inativo |
      | G7 | ramo `pending` desligado (volta a tratar `undefined` como falha) | card: "durante o carregamento, …"; página: "enquanto os agentes carregam, …" (2) |
      | G8 | `status` `error` vencendo o dado na mão (fora da lista original) | card: "com a lista na mão, uma falha posterior não esconde os agentes" (1) |

## 5. Verificação automatizada

- [x] 5.1 (`apps/frontend`) `npm run lint`, `npm run format:check`,
      `npm run test`, `npm run build`. Falha de `format:check` só é
      pré-existente se estiver na lista da 1.1. Se `prettier --write`
      reformatar arquivo fora do escopo, reverter antes de medir o diff.
      Feito (load 4,1 em 12 núcleos): `lint` **verde**; `format:check`
      reprova nos **mesmos 52 arquivos da baseline** (`diff` das duas listas
      vazio), nenhum desta change; `test` **verde**, 118 arquivos, **1444/1444**
      (baseline 1431 + 13 novos: 7 no card, 6 na página), 109,48s; `build`
      **verde**. O `prettier --write` foi rodado só nos cinco arquivos da
      change e só reformatou o card; nada fora do escopo.
      **Refeita depois da conferência**, porque a seção 6 mudou o card, a página
      e `index.css`: `lint` **verde**; `format:check` com os **mesmos 52** da
      baseline; `build` **verde**. O `test` da primeira tentativa **reprovou 7
      casos** em 117 arquivos, 584,96s — sete *timeouts* de 15s em arquivos
      que esta change não toca (`AgentKnowledgeTab`, `AgentToolsTab`,
      `AgentEditPage`, `InventoryPage`, `KnowledgeDocumentModal`,
      `KnowledgeBaseCreatePage`, `McpServerCreatePage`) e um worker do Vitest
      que não subiu (`AppShell.test.tsx`), com `load average` chegando a **62**
      (Chrome do usuário, Vite, VM do Podman). Não classificado como ambiental
      pela leitura: refeita **na mesma árvore** com o load em 5,3, deu
      **118 arquivos, 1444/1444, 108,44s** — o mesmo número e a mesma duração da
      medição anterior à conferência. Os guardas da 4.1 também foram
      **refeitos contra o código final** e reprovaram os mesmos testes.
- [x] 5.2 (raiz) `python3 scripts/check-docs.py`.
      Feito: `Integridade da documentação: OK`.
- [x] 5.3 (`apps/frontend`) Conferir que os testes existentes de abas,
      documentos, diagnóstico e modais passaram **sem mudança de asserção**
      (`git diff` do arquivo de teste da página só com acréscimos).
      Feito: `KnowledgeBaseDetailPage.test.tsx` com **81 linhas adicionadas e 0
      removidas**. No teste do card, a única asserção removida é a da D2
      (`getByText('Ativo')`); as outras remoções são chamadas de `renderCard`
      ganhando o `status` obrigatório, sem mudança de asserção (`design.md`,
      D7).

## 6. Conferência contra o protótipo (`design.md`, D6)

- [x] 6.1 (`apps/frontend`) Subir o painel contra `apps/api` local (com
      `TZ=America/Sao_Paulo`) e montar dados para cada estado: sem agente, um
      agente, sete ou mais, agente inativo. Os estados "carregando" e
      "indisponível" saem do próprio CDP, sem derrubar a API nem alterar dados:
      `Fetch.enable` filtrando `/agents`, e `Fetch.failRequest` (indisponível)
      ou a requisição pausada sem continuar pelo tempo da captura
      (carregando).
      Feito, com um desvio: o painel (Vite já em pé, servindo esta árvore)
      contra a `apps/api` local que já estava rodando (`:5017`, desta mesma
      árvore; o backend não muda nesta change). **Nenhum dado gravado:** a base
      local não tinha vínculo e só 5 agentes, então os estados com vínculo usam
      a resposta real de `GET /agents` acrescida pelo CDP do vínculo e de clones
      (`Fetch.fulfillRequest`) — `design.md`, D6 passo 1. "Sem agente" é a
      resposta real, sem interceptação.
- [x] 6.2 (`apps/frontend`) Percorrer com Chrome headless via CDP e capturar
      cada estado nas abas `Documentos` e `Diagnóstico do índice`, nos esquemas
      claro e escuro, a 1440px e a ~1860px — inclusive "carregando" e
      "indisponível".
      Feito: 7 estados (sem agente, um, inativo, sete, **doze** — acrescentado
      na rodada 2 porque sete não quebravam linha —, carregando, indisponível) ×
      2 abas × 2 esquemas × 2 larguras = **56 capturas**, mais 2 de foco, em
      `design/capturas/`, com as medidas de cada rodada em
      `design/capturas/medidas-r2.json` a `medidas-r4.json`. Nomes no formato
      `<estado>_<aba>_<esquema>_<largura>.png`. As capturas guardadas são as
      da rodada final (4).
- [x] 6.3 (`apps/frontend`) Comparar **dimensões** contra as pranchas 3a e 4a
      de `design/`: largura dos dois cards (cheia, igual à da barra), altura e
      raio do chip, vão entre chips, ponto de quebra a 1440 e a 1860, distância
      até a barra de abas. Conferir também o anel de foco do teclado no link do
      chip (Tab, claro e escuro) e a área clicável do chip de agente inativo
      (só o nome é link); registrar se é aceitável — se não for, PARAR e
      reportar antes de mudar a D2. Iterar até uma rodada sem achado. Capturas
      em `design/capturas/`, nomeadas por estado, aba, esquema e largura.
      Feito em **quatro rodadas**; a quarta sem achado. A primeira foi parcial
      (só sete agentes, 1440px, dois esquemas) e as medidas dela não foram
      salvas em arquivo — estão na tabela da D8. Achados e correções na
      tabela da D8 do `design.md`. Medidas finais, idênticas nas oito capturas
      de cada estado: descrição, card de agentes e barra com a **mesma largura**
      (1184px a 1440; 1604px a 1860); 16px entre os cards e **24px** até a
      barra (prancha: 18 e 24); chip de **28px**, raio 14px, vão de **8px**;
      sete chips numa linha, doze em **duas** linhas nas duas larguras; card de
      **94px** em todo estado de uma linha; o card nunca dentro do `tabpanel`.
      **Foco:** anel de 2px na pílula inteira, visível no claro e no escuro
      (`foco-chip_documentos_{claro,escuro}_1860.png`), alcançado com 11 Tabs.
      **Área clicável do chip inativo: aceitável** — link de 119×26 num chip de
      168×28; só o rótulo `Inativo` (47×15) não navega. A D2 não mudou.
- [x] 6.4 Validação manual pelo operador (convenção 14). O
      archive espera esta validação.
      Feita pelo dono em 02/10/2026: "testes manuais realizados com sucesso",
      sem ajuste pedido.

## 7. Registro

- [x] 7.1 (raiz, nenhum app) `02-HISTORICO_E_STATUS.md`: entrada da change e a
      correção de protótipo da D2 na lista viva "Correções de protótipo";
      registrar o achado da D5 conforme a decisão do mantenedor.
- [x] 7.2 (raiz, nenhum app) `CHANGELOG.md`, seção `[Unreleased]`.
- [x] 7.3 (raiz, nenhum app) Se a implementação divergir do `design.md`,
      corrigir o `design.md` com a causa real.
