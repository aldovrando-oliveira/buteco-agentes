# Tarefas — detalhe, listagem e filtro de base sincronizada (#107)

**As tarefas de código e verificação rodam em `apps/frontend`.** Nenhuma toca
`apps/api`, `apps/connectors`, `apps/workers` ou `apps/inbox`. As do grupo 9 rodam
na raiz do repositório (documentação) e não pertencem a app nenhum. Se a tela
precisar de algo que o backend não oferece, PARAR e reportar. Nada da #101
(histórico), da #131 (modal de documento) nem da #136 (exclusão de base): se o
escopo encostar nelas, registrar e não implementar.

**Worktree:** `/Users/aldovrando/Projetos/buteco-agents-107`, branch
`feat/107-detalhe-base-sincronizada`, criada sem upstream de `origin/main`
(`737ec2c`) em 04/10/2026, e atualizada em 04/10/2026 por fast-forward até `aff581e`
(merges da #138, PR #149, e da #47, PR #150), sem conflito de código: o único
conflito foi de seções acrescentadas no fim do `02`. Confirmar `pwd` e `git branch --show-current` antes de
cada grupo. Nenhum comando git no diretório principal nem nos worktrees da #138 e
da #47.

**Trabalho em paralelo:** a #138 e a #47 correm em outros worktrees e podem
editar os mesmos `01-ARQUITETURA_E_CONVENCOES.md`, `02-HISTORICO_E_STATUS.md`,
`CHANGELOG.md` e `docs/`. **Esses arquivos podem conflitar no merge.** Edições
curtas e localizadas: acrescentar seções, nunca reescrever trechos. Várias suítes
podem rodar ao mesmo tempo: conferir o `load average` antes de classificar
qualquer falha de teste, e com a máquina carregada esperar e refazer. Portas só na
faixa 57100–57199, cada uma conferida com `lsof -i :<porta>`; nunca derrubar
processo que esta change não iniciou.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/frontend`) `npm ci` neste worktree e a baseline antes de qualquer
      edição de código: `npm run lint`, `npm run format:check`, `npm run test`,
      `npm run build`, com o `load average` anotado. Registrar aqui o resultado de
      cada um e, para o `format:check`, a lista de arquivos que já reprovam — só
      esses contam como pré-existentes.
      Feito em 04/10/2026, em `737ec2c` sem nenhuma edição de código, depois de
      `npm ci` neste worktree.
      - `npm run lint`: **verde** (saída 0). Load 13,3.
      - `npm run format:check`: **reprova** (saída 1) em **52 arquivos**, a
        lista abaixo; dois são desta feature (`KnowledgeBaseEditPage.tsx` e o
        teste dele), e esta change não os edita.
      - `npm run test`: **verde** — 125 arquivos, **1580/1580**, 245,6s. Load 13,6
        no início e **78,3** no fim (outras sessões subiram suítes no meio).
      - `npm run build`: **verde**, só o aviso de chunk acima de 500 kB.

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

## 2. Tipo e funções puras do estado (`design.md`, D2, D5, D6)

- [x] 2.1 (`apps/frontend`) `types/knowledgeBase.ts`: `KnowledgeBaseSyncState`
      (`lastCompletedAt`, `lastFinishedAt`, `failingSince`, `lastError`,
      `ignoredFiles`) e `KnowledgeBaseIgnoredFile`; `syncState` obrigatório em
      `KnowledgeBase`, com a proveniência de `null` e de `[]` de `ignoredFiles` no
      comentário; trocar o comentário que dizia que a #107 o acrescentaria.
- [x] 2.2 (`apps/frontend`) Acrescentar `syncState` a todo teste que monta
      `KnowledgeBase` à mão, nesta e em outras features (`npm run build` acusa os
      que faltam).
- [x] 2.3 (`apps/frontend`) `utils/syncState.ts`: `syncStatus` (os quatro estados
      da D2), `isSyncFailing` (`failingSince !== null`), `formatSyncInstant` e
      `syncWaitInterval(request, base, now)` (4000 ou `false`, D5).
- [x] 2.4 (`apps/frontend`) `utils/syncState.test.ts`: os quatro estados, o caso
      inalcançável de `lastError` sem `failingSince` (trata como em dia), base
      manual, e cada ramo de `syncWaitInterval` (sem pedido, linha de base igual
      dentro do limite, diferente, nula que vira preenchida, além do limite).

## 3. Textos por código (`design.md`, D4)

- [x] 3.1 (`apps/frontend`) `utils/connectorErrors.ts`: exportar
      `sharedCodeSentence` e usá-la nas linhas de hoje sem mudar nenhum texto;
      acrescentar o contexto `sync-request` com os quatro códigos da tabela
      "Resposta do Sincronizar agora". Os testes de hoje continuam passando sem
      mudança.
- [x] 3.2 (`apps/frontend`) `utils/syncStateMessages.ts`: `syncFailureMessage` e
      `ignoredFileReason`, com as tabelas "Falha da base" e "Arquivo ignorado" da
      D4, a frase de garantia nas duas variantes, e o comentário de arquivo, linha
      e gatilho em cada afirmação sobre outro app (5 minutos, 1 MiB, Docs e `.md`).
- [x] 3.3 (`apps/frontend`) Testes: um caso por código das três tabelas, o código
      desconhecido de cada uma (texto neutro com o código, diferente de todos os
      conhecidos), `access-denied` com e sem `detail`, `rate-limited` sem
      "acesso" nem "compartilh", `download-blocked` com a orientação de leitores,
      `too-large` com o `detail` em bytes como veio, e a varredura de todos os
      textos montados sem `exclu`, `remov` nem `apag` (sem distinção de caixa).
- [x] 3.4 (`apps/frontend`) Guarda verificado (convenção 15): reintroduzir de
      propósito "Nenhum documento foi excluído" na garantia e ver a varredura
      reprovar; trocar o texto de `rate-limited` pelo de `access-denied` e ver a
      negativa reprovar; registrar aqui e reverter.
      Feito em 04/10/2026, um defeito de cada vez, restaurado da cópia e
      conferido por `cmp` e `grep`:
      - garantia "Nenhum documento foi excluído": reprovou
        `syncStateMessages.test.ts > nenhum texto manda excluir > nenhum texto de
        falha da base, de arquivo ignorado nem do pedido contém exclu, remov ou
        apag` com `expected 'Sem acesso à pasta A conta sync@exemp…' not to match
        /exclu|remov|apag/i` (e a igualdade do `access-denied`);
      - texto de `rate-limited` trocado pelo de `access-denied`: na primeira
        rodada reprovou só a **igualdade** do teste de `rate-limited`, porque ela
        vinha antes da negativa e a negativa nunca rodava (guarda no lugar errado,
        convenção 15). A negativa passou para a primeira linha do teste, e na
        segunda rodada reprovou `rate-limited é passageira, automática, e não fala
        de acesso` com `expected 'Limite de chamadas do Google A conta …' not to
        match /acesso|compartilh/i`.

## 4. Cliente e consultas (`design.md`, D5)

- [x] 4.1 (`apps/frontend`) `api/connectorsApi.ts`: `request<T>` devolve
      `undefined` em `202` e `204` sem ler corpo; `requestKnowledgeBaseSync(id)`
      (`POST /connectors/knowledge-bases/{id}/sync`, id escapado); trocar o
      comentário "PARA A #107".
- [x] 4.2 (`apps/frontend`) `api/connectorsApi.test.ts`: URL e método, `202` sem
      corpo resolvido, erro com `code`, falha de rede como `kind: "network"`, e
      nenhuma chamada sem a variável.
- [x] 4.3 (`apps/frontend`) `api/useConnectors.ts` e teste:
      `useRequestKnowledgeBaseSyncMutation`, sem `retry`.
- [x] 4.4 (`apps/frontend`) `api/useKnowledgeBases.ts`: `useKnowledgeBaseQuery(id,
      options?)` com `syncRequest` opcional (corrigido na implementação, D5); sem a
      opção, igual a hoje.
- [x] 4.5 (`apps/frontend`) Guarda pareado da convenção 20 em
      `useKnowledgeBases.test.ts`: a determinística, que resolve a opção real contra
      a query real do cache, e a comportamental com timers falsos (a consulta se
      repete enquanto `lastFinishedAt` não muda, para quando muda, e para no
      limite). Lembrar da armadilha do `notifyOnChangeProps: 'tracked'` registrada
      na convenção 20 (tocar `.data` na primeira espera).
      Feito em 04/10/2026. Os quatro testes do bloco "acompanhamento da
      sincronização" tocam `.data` desde a primeira espera. Guarda verificado, um
      defeito de cada vez, restaurado da cópia e conferido por `cmp` e `grep`:
      - hook ignorando o pedido (`refetchInterval: false`): reprovaram
        `determinística: 4 s enquanto lastFinishedAt é a linha de base, false
        quando muda` (`expected false to be 4000`), `comportamental: repete
        enquanto espera e para quando lastFinishedAt muda` (`expected 1 to be
        greater than 1`) e `comportamental: para no limite de 5 minutos sem
        mudança` (`expected 1 to be greater than 60`);
      - intervalo constante enquanto houver pedido (`syncRequest ? 4000 :
        false`): reprovaram os mesmos três, com `expected 4000 to be false`,
        `expected 8 to be 3` e `expected 92 to be 77`.

## 5. Componentes do detalhe (`design.md`, D1, D2, D3, D5)

- [x] 5.1 (`apps/frontend`) `KnowledgeDocumentsCard`: prop `readOnly`; com ela, sem
      "Adicionar documento", sem coluna "Ações", cabeçalho "Somente leitura — o
      conteúdo vem da pasta" e estado vazio sem convite a adicionar; "Reindexar
      documento" continua na faixa de falha.
- [x] 5.2 (`apps/frontend`) `KnowledgeDocumentsCard.test.tsx`: as negativas com
      `readOnly` (nenhum dos três controles, nenhum cabeçalho "Ações"), a positiva
      sem `readOnly`, e "Reindexar documento" presente nos dois.
      Vistos reprovando antes do código (as duas negativas; a positiva já
      passava, porque é o comportamento de hoje). Dois testes existentes que
      renderizam o card sem o auxiliar ganharam só `readOnly={false}`, porque a
      prop é obrigatória pela D1; nenhuma asserção mudou.
- [x] 5.3 (`apps/frontend`) `KnowledgeBaseSyncOriginCard` apresentacional: as três
      colunas e os quatro estados da D2, o alerta da D4, o botão com rótulo por
      estado e indisponível com a explicação, as fases do pedido (carregando,
      solicitada, concluída, terminada com falha, sem resultado no limite, erro do
      pedido). Recebe dados e callbacks; não importa hook.
- [x] 5.4 (`apps/frontend`) `KnowledgeBaseSyncOriginCard.test.tsx`: um caso por
      estado e por fase; "nunca sincronizou" sem "falha" nem alerta; rótulo da
      pasta nos dois estados de falha; provedor desconhecido; nenhum texto de
      conclusão na fase "solicitada".
      **Desvio de processo:** escrito antes do componente, mas só rodado depois
      dele; não foi visto reprovando. O card de ignorados (5.5) foi visto
      reprovando (`Failed to resolve import "./KnowledgeBaseIgnoredFilesCard"`)
      antes de existir, e as negativas da página (grupo 6) cobrem este card pelo
      guarda verificado da 6.4.
- [x] 5.5 (`apps/frontend`) `KnowledgeBaseIgnoredFilesCard` e teste: os três
      estados da D3 com as negativas cruzadas de nulo e vazio, a contagem no
      cabeçalho, e a linha da última sincronização concluída com a base falhando.

## 6. Página de detalhe (`design.md`, D1, D5, D7, D10; specs `knowledge-base-sync-state-ui` e `knowledge-document-catalog-ui`)

- [x] 6.1 (`apps/frontend`) `KnowledgeBaseDetailPage`: card de origem entre a
      descrição e os agentes, card de ignorados na aba Documentos, `readOnly`
      pela origem, modais de documento só em base manual, e o pedido de
      sincronização da D5 (releitura antes do `POST`, linha de base,
      `refetchInterval`, fim por mudança ou por limite, invalidação de documentos
      e do resumo no fim).
- [x] 6.2 (`apps/frontend`) `KnowledgeBaseDetailPage.test.tsx` (base manual): os
      testes de hoje passam só com `syncState: null` acrescentado; um teste novo
      afirma a ausência do card de origem e do de ignorados, e a presença de
      "Adicionar documento", "Atualizar" e "Excluir".
      Feito: os testes de hoje passam só com `syncState: null`. O teste novo da
      base manual ficou no `KnowledgeBaseDetailPage.sync.test.tsx` (bloco "base
      manual"), e não neste arquivo, porque ele também afirma a negativa de rede
      ("nenhuma chamada ao `apps/connectors`"), que só o `fetch` interceptado
      sustenta.
- [x] 6.3 (`apps/frontend`) `KnowledgeBaseDetailPage.sync.test.tsx`, com o `fetch`
      global interceptado e sem mock de módulo, os aceites da issue:
      - base sincronizada sem "Adicionar documento", "Atualizar" e "Excluir";
      - "nunca sincronizou" sem falha no card, e `ignoredFiles` nulo sem "Nenhum
        arquivo";
      - `access-denied` com o e-mail e `rate-limited` sem "acesso";
      - "Sincronizar agora": `202` com `lastFinishedAt` igual mostra "solicitada",
        botão desabilitado e nenhum texto de conclusão; a troca de
        `lastFinishedAt` mostra a conclusão e refaz a listagem de documentos; o
        limite com timers falsos; a linha de base vinda da releitura; falha de rede
        com o texto de fora do ar;
      - sem `VITE_CONNECTORS_BASE_URL` (`vi.stubEnv`): botão desabilitado com a
        explicação, alerta de falha presente, e **nenhuma** requisição ao
        `apps/connectors` no `fetch` interceptado, conferindo antes que ele
        registrou as chamadas ao `apps/api`.
- [x] 6.4 (`apps/frontend`) Guarda verificado (convenção 15): devolver o botão
      "Adicionar documento" em base sincronizada e ver reprovar; mostrar a
      conclusão logo no `202` e ver reprovar; tirar o limite e ver o teste do limite
      reprovar; registrar aqui e reverter.
      Os 11 testes novos foram vistos reprovando antes da página (`Unable to find
      an element by: [data-testid="sync-origin-card"]`; o de base manual já
      passava, é o comportamento de hoje). Um deles tinha defeito próprio (dois
      documentos com o mesmo título) e foi corrigido antes. Guarda verificado em
      `KnowledgeBaseDetailPage.sync.test.tsx`, um defeito de cada vez, restaurado
      da cópia e conferido por `cmp`:
      - "Adicionar documento" de volta em base sincronizada (`readOnly={false}`
        com `onAdd`): reprovou `não oferece adicionar, atualizar nem excluir, e
        explica de onde vêm os documentos` com `expect(element).not.toBeInTheDocument()`;
      - conclusão logo no `202` (fase "finished" sem comparar com a linha de
        base): reprovaram `202 sem mudança: solicitada, botão desabilitado, e
        nenhum texto de conclusão`, `a troca de lastFinishedAt…`, `a linha de
        base vem da releitura…` e `sem resultado em 5 minutos…`, com `Unable to
        find an element with the text: Sincronização solicitada. Aguardando o
        resultado…`;
      - sem o temporizador do limite: reprovou `sem resultado em 5 minutos: para
        de consultar e não afirma sucesso nem falha` com
        `expect(element).toHaveTextContent()`.

## 7. Listagem e filtro (`design.md`, D6; spec `knowledge-base-catalog-ui`)

- [x] 7.1 (`apps/frontend`) `KnowledgeBaseTable`: linha de origem ("Manual" ou
      "{provedor} › {pasta}") e linha "Sincronização falhando desde …" na cor de
      `statusPresentation('Failed')`, sem coluna nova e sem mudar as larguras.
- [x] 7.2 (`apps/frontend`) `KnowledgeBaseTable.test.tsx`: manual, sincronizada em
      dia, falhando, e "nunca sincronizou" sem linha de falha.
- [x] 7.3 (`apps/frontend`) `KnowledgeBaseListPage`: `STATUS_FAILED` com
      `hasFailure(resumo) || isSyncFailing(base)`; `effectiveStatusFilter` e a opção
      desabilitada sem resumo continuam como hoje.
- [x] 7.4 (`apps/frontend`) `KnowledgeBaseListPage.test.tsx`: base inativa só com
      sincronização falhando entra no filtro; sem resumo, a opção continua
      desabilitada mesmo com sincronização falhando e a listagem mostra todas.
- [x] 7.5 (`apps/frontend`) Guarda verificado: tirar `isSyncFailing` do filtro e ver
      o teste reprovar; filtrar só pela sincronização sem o resumo e ver o teste
      da opção desabilitada reprovar; registrar e reverter.
      Os testes novos (4 da tabela e 1 do filtro; os outros 3 afirmam o que já
      valia) foram vistos reprovando antes do código, com `Unable to find an
      element by: [data-testid="origem-…"]` e `Unable to find an accessible
      element with the role "link" and name "Políticas internas de RH"`.
      **Incidente registrado:** a primeira rodada destes dois arquivos reprovou
      os 45 testes em 0 ms. A causa não foi carga: o comando subiu quatro níveis
      a partir de `knowledge-bases` e rodou em `apps/`, sem a configuração do
      vitest (sem jsdom). Ele deixou um cache em `apps/node_modules/.vite`
      (ignorado pelo git, criado às 16:37 por esta sessão), que foi apagado; o
      `npx prettier` do mesmo comando baixou um prettier para o cache do npx e não
      escreveu nada. Daí em diante, só caminho absoluto. O `prettier --write` na
      pasta da feature reformatou também os dois arquivos que já reprovavam no
      baseline: `KnowledgeBaseEditPage.tsx` foi restaurado do `HEAD` com `git
      show`, e no teste dele ficou só a adição de `syncState` da 2.2.
      Guarda verificado, um defeito de cada vez, restaurado da cópia e conferido
      por `cmp`:
      - filtro sem `isSyncFailing`: reprovou `inclui base inativa só com
        sincronização falhando` com `Unable to find an accessible element with
        the role "link" and name "Políticas internas de RH"`;
      - opção habilitada sem o resumo quando há sincronização falhando: reprovou
        `sem o resumo, continua desabilitado mesmo com sincronização falhando` com
        `expect(element).toBeDisabled()`.

## 8. Verificação técnica e conferência visual (`design.md`, D9)

- [x] 8.1 (`apps/frontend`) `npm run lint`, `npm run format:check` (só os arquivos
      da baseline podem reprovar), `npm run test` e `npm run build`, com o `load
      average` anotado; registrar os números medidos aqui.
      Medido duas vezes. Depois do grupo 7 (load 3,6 no início): lint verde,
      `format:check` com a **mesma** lista de 52 arquivos do baseline (conferida
      por `diff`), **130 arquivos, 1683/1683** em 97,0s, build verde. Depois das
      correções da conferência (load 4,2 no início, 40,7 no fim do build): lint
      verde, `format:check` com a mesma lista de 52, **130 arquivos, 1685/1685**
      em 147,1s, build verde. Contra o baseline (125, 1580): +5 arquivos (os cinco
      novos) e +105 testes, conferidos pelos testes novos — 15 (`syncState`), 25
      (`syncStateMessages`), 15 (card de Origem), 6 (card de ignorados), 12
      (página sincronizada), e nos arquivos existentes 9 (`connectorErrors`), 5
      (`connectorsApi`), 1 (`useConnectors`), 4 (`useKnowledgeBases`), 5 (card de
      documentos), 5 (tabela), 2 (listagem) e 1 (`theme`).
- [x] 8.2 (`apps/frontend`) Subir o painel para a conferência: duas instâncias do
      Vite em portas livres da faixa 57100–57199, uma com e outra sem
      `VITE_CONNECTORS_BASE_URL`, os dois endereços de backend em portas da faixa
      sem processo; Chrome headless por CDP com respostas montadas. Registrar
      portas, PIDs e o encerramento de tudo o que foi iniciado.
      Feito em 04/10/2026, cada porta conferida com `lsof` antes:
      - 57110: Vite com `VITE_CONNECTORS_BASE_URL=http://localhost:57121` (`npm
        exec` PID 79876, `node` PID 79913);
      - 57111: Vite sem a variável, e sem `.env` no worktree (`npm exec` PID
        79877, `node` PID 79911);
      - 57120 (`apps/api`) e 57121 (`apps/connectors`): nenhum processo, toda
        resposta montada pelo `Fetch` do CDP; 57122 só como `VITE_INBOX_BASE_URL`,
        sem uso nestas telas;
      - 57130: Chrome 154 headless com perfil próprio no scratchpad (PID 79946).
      Os cinco processos foram encerrados ao fim, e as cinco portas conferidas
      livres por `lsof`. O preflight `OPTIONS` foi atendido pelo `Fetch`, sem
      `--disable-web-security`.
- [x] 8.3 (`apps/frontend`) Percorrer por CDP os estados da D9, claro e escuro, a
      1440px e a ~1860px; capturas em `design/capturas/`.
      16 estados × 2 esquemas × 2 larguras = 64 capturas, em
      `design/capturas/<estado>_<esquema>_<largura>.png`. O "sem resultado no
      limite" encurta, só naquela página, os `setTimeout` de 60 s ou mais para
      1,5 s antes do clique (D9).
- [x] 8.4 (`apps/frontend`) Comparar as dimensões da D9 contra as pranchas 1, 4a e
      4b, iterar até uma rodada sem achado, e registrar as rodadas e as medidas em
      `design/capturas/medidas-r<N>.json`; achado que contrarie a prancha vira
      linha da D8.
      Duas rodadas (`medidas-r1.json`, `medidas-r2.json`), a segunda sem achado.
      R1-1 (larguras da tabela somente leitura) e R1-2 (verde de sucesso a
      3,89:1 no claro) corrigidos, com teste antes do código, e registrados na
      D9; nenhum contraria a prancha, então nenhum vira linha da D8. O contraste
      de 4,14:1 do texto `dimmed` na faixa de cabeçalho é anterior a esta change
      (o contador "2 agentes" mede o mesmo) e virou a **#148**.
- [x] 8.5 Validação manual pelo mantenedor (convenção 14). O archive espera por
      ela.
      Feita pelo dono em 05/10/2026 ("validação feita"), sem ajuste pedido, numa
      pilha local deste worktree com respostas reais: Postgres (pgvector) e
      RabbitMQ próprios no Podman (57140, 57141/57142), `apps/api` (57150),
      `apps/connectors` (57151) com a chave da service account de validação e o
      ciclo ligado a este `apps/api`, `apps/workers` com o gateway de embedding
      (`qwen-qwen3-embedding-8b`, 4096 dimensões, conferido antes) e o painel
      (57152). Pilha derrubada ao fim: processos encerrados e contêineres
      removidos com os volumes.

## 9. Registro

- [x] 9.1 (raiz, nenhum app) `02-HISTORICO_E_STATUS.md`: entrada da change e as
      correções da D8 na lista viva "Correções de protótipo", em seção acrescentada.
- [x] 9.2 (raiz, nenhum app) `CHANGELOG.md`, seção `[Unreleased]`, entrada curta.
- [x] 9.3 (raiz, nenhum app) Se a implementação divergir do `design.md`, corrigir o
      `design.md` com a causa real.
      Feito: D4 e C6 com a decisão do mantenedor sobre a garantia; D5 com o
      `syncRequest` no lugar da função `refetchInterval` montada pela página, e a
      causa; D9 com o resultado das duas rodadas e o que foi medido e não
      corrigido; a árvore com `theme.ts` e `theme.test.ts`, que entraram pela
      conferência (R1-2); a C8 com a contagem certa de códigos.
- [x] 9.4 (raiz, nenhum app) `python3 scripts/check-docs.py` e
      `openspec validate frontend-detalhe-base-sincronizada --strict`.
      Em 04/10/2026: `Integridade da documentação: OK` e `Change
      'frontend-detalhe-base-sincronizada' is valid`.
