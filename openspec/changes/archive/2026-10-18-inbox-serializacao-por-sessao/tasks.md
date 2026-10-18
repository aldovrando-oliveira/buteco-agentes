> **Trilha paralela.** Esta change roda na worktree
> `../buteco-agentes-inbox-serializacao-por-sessao`, branch
> `fix/162-inbox-serializacao-por-sessao` (nome da convenção 24; nasceu como
> `change/inbox-serializacao-por-sessao` e foi renomeada antes de qualquer push), a
> partir de `cb736db` (`origin/main` em 18/10/2026, merge do #172). Nasceu sobre
> `18a2f3b` e foi rebaseada antes do apply. Os 10 commits do rebase (#169, #172)
> não tocam `apps/inbox`, `tests/InboxOrchestratorRoundTrip.Tests`,
> `AgentExecutionService` nem `ConversationContextLock` (a 1.2 confirma no código). A
> `workers-consumo-concorrente` pode correr em paralelo. Os conflitos esperados são
> no `02-HISTORICO_E_STATUS.md`, em `ConversationContextLockFailureTests.cs` e talvez
> em `tests/InboxOrchestratorRoundTrip.Tests`; quem mergear por último faz rebase.
> **Esta chega primeiro à `main`** (#162).
>
> **Antes e depois de cada suíte:** `podman ps` e `uptime` na mesma frase do número,
> e amostra de `vstest` de outras worktrees durante a rodada. Variáveis:
> `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`,
> `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`;
> `--blame-hang-timeout 4m`.
>
> O código de produção é só em **`apps/inbox`**. Testes em `apps/inbox`,
> `tests/InboxOrchestratorRoundTrip.Tests` e uma nota em `apps/workers/tests`. Nada
> em `apps/api`, `apps/workers/src`, `apps/connectors`, `apps/frontend` nem `libs/`.

## 1. Baseline, antes de tocar código

- [x] 1.1 Suítes sobre `cb736db` (a base depois do rebase; não `18a2f3b`), por classe, com as condições da VM:
  `apps/inbox`, `apps/workers` (a change não toca `src`, então o número tem de se
  repetir no fechamento) e `InboxOrchestratorRoundTrip`. O número vem da medição,
  nunca de memória nem do `02`.
  - **Medido em 18/10/2026, sobre `cb736db`, as três em sequência**, com
    `buteco-agents_postgres_1` e `buteco-agents_rabbitmq_1` de pé antes e depois de
    cada uma, e **zero** processos `vstest`/`testhost` de outras worktrees em todas as
    amostras (a cada 30 s):
    - `apps/inbox`: **241/241**, 42 s (load 4,03 antes, 5,68 depois). Por classe:
      `DebounceSweepServiceTests` 10, `DispatchReconciliationTests` 20,
      `PushNotificationEndpointsTests` 12, `DebounceRestartAndConcurrencyTests` 2,
      `DebounceMessageInstantTests` 2, `InboundMessageOrchestratorTests` 6, demais 189
      em 25 classes.
    - `apps/workers`: **405/405**, 6m06s (load 5,68 antes, 2,65 depois; picos de 10,35
      durante). `ConversationContextLockFailureTests` 5.
    - `InboxOrchestratorRoundTrip`: **10/10**, 9 s (load 2,52 antes, 5,82 depois).
      `RoundTripTests` 4, `OrphanDispatchRoundTripTests` 6.
- [x] 1.2 Reler `DebounceSweepService.cs:78-81` e `:133-136` e
  `AppDbContext.cs:126-128`, e confirmar que nada mudou na `main` desde o
  `design.md` (convenção 9). Divergência corrige o design antes do código.
  - **Premissa da prova da D1:** só o sweep devolve uma linha a `Pending`; a
    reconciliação e o endpoint de push só removem linhas. Conferir no código e
    citar as linhas. Lido em 17/10/2026 sobre `18a2f3b` (`apps/inbox/src/Buteco.Inbox/`):
    - a única escrita de `Status = Pending` fora do construtor
      (`Orchestration/Entities/PendingDispatch.cs:54`) é `RegisterTransportFailure`
      (`:85`), chamada só por `DebounceSweepService.cs:311`;
    - endpoint de push: `PushNotificationEndpoints.cs:77` chama
      `DispatchOutcomeProcessor.CompleteFromTaskAsync`, que termina em `CloseAsync`,
      e `CloseAsync` remove a linha (`DispatchOutcomeProcessor.cs:98`). O endpoint
      não altera `Status`;
    - reconciliação: `ClaimForReconciliation` (`DispatchReconciliationService.cs:266`;
      `PendingDispatch.cs:97-101`) troca só `ExpectedToken` e
      `ReconciliationClaimedAt`. Em seguida, `CompleteFromTaskAsync` (`:216`) ou
      `FailAsync` (`:247`) removem a linha pelo mesmo `CloseAsync` (`:98`);
    - `MarkFailed` (`PendingDispatch.cs:105`) só é chamado em
      `DebounceSweepService.cs:320`, seguido de `FailAsync`, que remove a linha.

    Na releitura do apply, repetir a busca (`Status = PendingDispatchStatus`,
    `RegisterTransportFailure(`, `MarkDispatching(`, `MarkFailed(`, `Remove(`) e
    registrar se algo mudou.

    **Relido em 18/10/2026 sobre `cb736db`: nada mudou.** `DebounceSweepService.cs:78-81`
    e `:133-136`, `AppDbContext.cs:126-128` e todas as linhas citadas acima conferem. A
    busca acha as mesmas escritas: `Status = Pending` só em `PendingDispatch.cs:54` e
    `:85`; `MarkDispatching` só em `DebounceSweepService.cs:146`; `MarkFailed` só em
    `:320`; `Remove` de linha só em `DispatchOutcomeProcessor.cs:98`. Nenhum
    `ExecuteUpdate`/`ExecuteDelete`/SQL cru sobre `pending_dispatches`. Os 92
    arquivos do rebase (#169, #172) não tocam `apps/inbox`,
    `tests/InboxOrchestratorRoundTrip.Tests`, `AgentExecutionService` nem
    `ConversationContextLock` (`git diff --stat 18a2f3b cb736db`).
    - Fato a mais, que a D3 usa: `AppendMessage` (o único outro escritor de
      `LastMessageAt`) só é chamado sobre a `Pending` (`FindPendingAsync`,
      `InboundMessageOrchestrator.cs:147-149`). O `LastMessageAt` de uma linha em
      `Dispatching` fica congelado, e a idade medida por ele só cresce.

## 2. Guardas vermelhos contra o código atual (testes)

- [x] 2.1 `apps/inbox`, `DebounceSessionSerializationTests` (novo), guarda 1 da D5:
  com a primeira linha em `Dispatching` (fake A2A em `Submitted`), duas mensagens
  fora da janela não geram segundo `SendMessage`. Depois da resolução pelo endpoint
  de push real, sai **um** `SendMessage` com `"msg2\nmsg3"`. **Vermelho no código
  atual:** registrar a asserção que reprova e o texto que saiu.
  - msg2 e msg3 chegam com 900 ms entre si (janela de 300 ms + 2 varreduras de 50 ms,
    com folga), então só a serialização junta as duas: a janela sozinha não junta.
  - **Vermelho em 18/10/2026 sobre `cb736db`** (2 s):
    `SecondAndThirdMessages_WhileFirstIsDispatching_LeaveAsOneBatchAfterPushResolvesFirst`
    reprova em `DebounceSessionSerializationTests.cs:65`, na asserção do "enquanto
    ocupada": esperado `["msg1"]`, saiu `["msg1", "msg2", "msg3"]`. São três
    `SendMessage` no mesmo contexto, cada mensagem sozinha, com a primeira ainda em
    `Dispatching`.
- [x] 2.2 `apps/inbox`, mesma classe, guarda 3 da D5: com a sessão A em
  `Dispatching`, a `Pending` vencida da sessão B sai no ciclo. Verde no código atual
  (é guarda de regressão contra um filtro global). Escrever isso no XML doc.
  - `OtherSessionDispatching_DoesNotHoldExpiredPendingOfThisSession`: **verde** em
    18/10/2026 sobre `cb736db`, na mesma rodada da 2.1. XML doc escrito.
- [x] 2.3 `tests/InboxOrchestratorRoundTrip.Tests`: controles da fixture, neutros por
  padrão. São o atraso do LLM, o `Command Timeout` da connection string do worker e a
  segunda instância de worker, no padrão de `PushGate` e `ChatClientFails`.
  Conferir que os testes existentes continuam verdes com os controles neutros.
  - Feito em `Support/RoundTripFixture.cs`: `ChatClientDelay` (zero),
    `WorkerCommandTimeoutSeconds` (nulo: o default de 30 s do Npgsql; vale para os
    hosts construídos depois de atribuído) e `StartSecondWorkersAsync` /
    `StopSecondWorkersAsync` (sem segunda instância). O `DisposeAsync` para a segunda
    instância, se houver.
  - Em 18/10/2026, com os controles neutros, os 10 existentes **verdes**
    (`RoundTripTests` 4, `OrphanDispatchRoundTripTests` 6), na mesma rodada do
    vermelho da 2.4 (41 s, load 4,01 → 6,70, zero `vstest` de outras worktrees).
- [x] 2.4 `tests/InboxOrchestratorRoundTrip.Tests`, `SessionSerializationRoundTripTests`
  (novo), guarda 4 da D5: duas instâncias de worker, `Command Timeout` curto, LLM
  mais lento que ele. msg1, espera a task em `Working`, sobe a segunda instância,
  msg2. Afirma as duas tasks `Completed`, as duas respostas entregues e nenhum
  `FailureNoticeText`. **Vermelho no código atual:** `[Completed, Failed]` e o aviso
  entregue. Registrar o que saiu.
  - LLM de 8 s, `Command Timeout=3` na segunda instância: os números de
    `ConversationContextLockFailureTests`. A asserção é um retrato só (estados das
    duas tasks + saídas entregues, em ordem), para a falha mostrar tudo de uma vez.
  - **Vermelho em 18/10/2026 sobre `cb736db`** (10 s), em
    `SessionSerializationRoundTripTests.cs:101`: esperado
    `[COMPLETED, COMPLETED, resposta, resposta]`, saiu
    `[TASK_STATE_COMPLETED, TASK_STATE_FAILED, "aviso de falha", resposta]`. O aviso
    de falha chegou ao contato **antes** da resposta da primeira mensagem.

## 3. Serialização por sessão (`apps/inbox`)

- [x] 3.1 `DebounceOptions.SessionSerializationMaxWait`, default de 10 min no tipo
  (D3), com o XML doc trazendo a derivação (#134: 400 s; delegação: 120 s; igual a
  `UntrackedDispatchMaxAge`), o fato de não ser medido e o gatilho de recalibração.
  - Feito em `Orchestration/DebounceOptions.cs`. O XML doc também registra o intervalo
    real do teto (9 min 50 s a 10 min, porque conta de `LastMessageAt`).
- [x] 3.2 Checagem no boot (convenção 8): valor menor ou igual a zero reprova a
  inicialização, com mensagem que nomeia `Debounce:SessionSerializationMaxWait` e o
  valor. Sem correção automática. Linha de log de sucesso quando passa. Chamada em
  `Program.cs`, no padrão das checagens que já existem lá.
  - Também reprova teto menor ou igual a `Debounce:Window`, pelo mesmo argumento
    (toda linha em `Dispatching` já nasceria além do teto; D3). A mensagem nomeia a
    chave, o valor e o `Window` vigente.
  - Teste em `DebounceOptionsValidationTests`, com os pares:
    - zero e negativo reprovam;
    - igual ao `Window` e menor que ele (positivo) reprovam;
    - maior que o `Window` passa, e loga o teto conferido.
  - Se a checagem rodar sobre o host construído, registrar no XML doc se algum teste
    prova o **registro** em `Program.cs`, ou dizer que não prova (precedente de
    `EmbeddingBatchSizeValidation`).
  - Feito em `Orchestration/DebounceOptionsValidation.cs`, chamado em `Program.cs` logo
    depois de `builder.Build()`. `DebounceOptionsValidationTests`: 7 casos (zero e
    `-00:00:01`; igual ao `Window` e `00:00:05`; `00:00:11` passa e loga o teto; os
    defaults passam).
  - **O registro é provado**, ao contrário do precedente do worker: os testes do inbox
    sobem a aplicação real pela `WebApplicationFactory`, e
    `InvalidMaxWait_FailsTheRealApplicationStartup` sobe o `Program.cs` com teto zero.
    Verde em 18/10/2026; a segunda perna (tirar a linha do `Program.cs` e ver
    reprovar) fica na 6.2. O XML doc diz isso.
- [x] 3.3 `DebounceSweepService.ProcessDueDispatchesAsync`: o predicado da D1 entra
  na consulta de candidatos. É um `NOT EXISTS` da mesma `SessionId` em
  `Dispatching`, com `LastMessageAt` dentro do teto, na mesma consulta. Comentário
  apontando para o `design.md`, D1 e D3.
  - Feito: `WhereSessionHasNoDispatchInFlight` aplicado sobre a consulta de candidatos.
    O corte é `LastMessageAt > agora - SessionSerializationMaxWait`.
- [x] 3.4 `DebounceSweepService.TryDispatchAsync`: o mesmo predicado na releitura
  antes da reivindicação. O comentário diz por que basta, sem lock nem índice (D1,
  "Por que a dupla checagem basta"), para ninguém "simplificar" tirando uma das duas.
  - Escrever um predicado só, usado nos dois pontos, para eles não divergirem.
  - Feito: o mesmo `WhereSessionHasNoDispatchInFlight` na releitura, com o argumento
    da D1 no comentário.
- [x] 3.5 `apps/inbox`, `DebounceSessionSerializationTests`, guarda 2 da D5: com o
  teto curto, a linha em `Dispatching` mais velha que ele não bloqueia; o par, mais
  nova que ele, bloqueia.
  - **Desvio do texto, com motivo:** em vez de encurtar o teto e esperar por ele, os
    dois testes leem o teto em vigor (o default de produção, 10 min) e semeiam a linha
    em `Dispatching` com `LastMessageAt` 1 min além do teto ou 1 min aquém dele. O
    guarda fica sem relógio e exercita o valor de produção. A linha semeada tem
    `TaskId`, para a reconciliação não a encerrar no meio do teste (o fake responde
    `TaskNotFound`, e a linha segue em `Dispatching`). Esse teste também afirma que a
    linha antiga não é alterada (spec, "o ciclo antigo continua em processamento,
    inalterado").
  - `DispatchingOlderThanMaxWait_NoLongerHoldsExpiredPending_AndIsLeftUntouched` e
    `DispatchingWithinMaxWait_StillHoldsExpiredPending`: verdes com a correção.
- [x] 3.6 `DebounceSweepServiceTests.MessagesOutsideWindow_TriggerSeparateSendMessageCalls`
  (guarda 5 da D5): antes de mudar, rodar com a correção e registrar que reprova.
  Depois, passar a resolver o primeiro disparo antes de esperar o segundo, com
  comentário dizendo por quê.
  - **Com a correção e sem o ajuste, reprova** (18/10/2026): `TimeoutException` em
    `DebounceSweepServiceTests.cs:112`, o `PollUntil` que espera o segundo
    `SendMessage`, que não sai com a primeira linha em `Dispatching`. Os outros 9 da
    classe ficam verdes na mesma rodada.
  - Ajustado: o primeiro ciclo é encerrado pelo endpoint de push real
    (`ResolveThroughPushAsync`) antes da segunda mensagem, com comentário.
- [x] 3.7 2.1 e 2.4 passam a verdes, 2.2 continua verde.
  - 18/10/2026, com a correção: `apps/inbox` **252/252** (241 da baseline + 4 de
    `DebounceSessionSerializationTests` + 7 de `DebounceOptionsValidationTests`), 38 s.
    `InboxOrchestratorRoundTrip` **11/11** (10 + `SessionSerializationRoundTripTests`),
    18 s. Nenhum outro teste do inbox dependia do defeito: só o da 3.6 mudou.

## 4. Nota no worker (`apps/workers/tests`)

- [x] 4.1 `ConversationContextLockFailureTests.TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState`:
  só o XML doc (D4). O `[Completed, Failed]` é o contrato do worker para cliente A2A
  externo. O inbox não produz mais o cenário desde esta change, e o guarda dele é
  `SessionSerializationRoundTripTests`. Nenhuma asserção muda.
  - Feito: o comentário `//` que dizia "é o cenário do contato que manda a segunda
    mensagem" virou XML doc na forma temporal (até a #162 / desde a #162), com o
    contrato para cliente A2A externo. `apps/workers` 405/405 no fechamento, as 42
    classes iguais à baseline.

## 5. Documentação (antes de qualquer archive; entra no PR)

- [x] 5.1 `02-HISTORICO_E_STATUS.md`, entrada da change: o que entrou, o que a
  implementação mediu, os guardas com a segunda perna de cada um, e o que ficou de
  fora. Na fila de "Próximo passo" (a única cópia viva), registrar a posição: antes
  da `workers-consumo-concorrente`, como pré-requisito de merge, e antes da
  `replicas-de-worker`. A **#164** (colisão de transporte) entra logo depois desta,
  sem bloquear o merge dela: o corpo da issue diz isso, mas a fila viva é o `02`
  (convenção 24).
  - Feito: entrada `inbox-serializacao-por-sessao` no fim do `02`, e "ACRÉSCIMO DE
    18/10/2026" logo abaixo do bloco da fila em "Próximo passo". **Escrita depois da
    6.1 e da 6.2, fora da ordem do arquivo**, porque a entrada leva a segunda perna
    de cada guarda, que só sai na 6.2. A #164 fica logo depois desta. A ordem entre
    a #164 e a #163 ficou em aberto no apply e foi **decidida pelo dono na revisão
    (18/10/2026)**: independentes, em paralelo, a #164 não bloqueia o merge da #163,
    e só a #162 vem antes das duas. O acréscimo da fila diz isso.
- [x] 5.2 `02`, item com **gatilho** e **posição**: uma linha em `Dispatching` sem
  `TaskId` bloqueia a sessão até o teto da D3 (~10 min), e a reconciliação a
  encerra como perda (~11 min).
  - **Gatilho:** o primeiro log "Mensagem de usuário perdida: disparo da sessão … sem
    TaskId" em produção, ou a #164 (colisão de transporte) entrar em andamento.
  - Feito, em "Itens abertos que esta change cria" da entrada. **Posição:** dentro da
    #164.
- [x] 5.3 `02`, item com **gatilho** e **posição** (achado lateral, `apps/workers`):
  o lock de contexto é adquirido depois de `StartWorkAsync`
  (`AgentExecutionService.cs:245` contra `:283`). A espera pelo lock aparece como
  `working`, e contamina as métricas de tempo de fila e de lock.
  - **Posição:** change própria, com deploy **junto** do deploy da
    `workers-consumo-concorrente`. As duas mudam o significado das séries de tempo de
    fila e de lock. Num deploy só, a quebra das séries é **um marco só**
    (convenção 22), e não dois marcos próximos que ninguém separa depois.
  - **Issue:** **#166** (convenção 23). O gatilho registrado antes, a abertura da
    `workers-consumo-concorrente`, disparou com a #163.
  - Feito, no mesmo bloco da entrada, e a #166 também no acréscimo da fila.
- [x] 5.4 `docs/architecture.md`, seção do `PendingDispatch` (`:340` em `cb736db`;
  era `:326` em `18a2f3b`): a serialização
  por sessão, o lote e o teto. Na seção do lock de contexto, o contrato da D2 para
  cliente A2A externo.
  - Feito: parágrafo "Serialização por sessão" no `PendingDispatch`, com o lote, o
    teto e o custo.
  - **A seção do lock de contexto não existia** (o lock só aparecia de passagem em
    `docs/deployment.md:381`). Entrou a subsecção nova "Execuções concorrentes no
    mesmo contexto", no fim de "Histórico de conversa e compactação", onde a
    conversa já é definida como as tasks do mesmo `contextId`.
  - A mais, pelo mesmo motivo da D2: um parágrafo em `docs/a2a-integration.md`, que é
    o que o cliente externo lê, apontando para a subsecção nova.
- [x] 5.5 `docs/configuration.md`: `Debounce__SessionSerializationMaxWait`, ao lado de
  `Debounce__*`, com o padrão e a derivação.
  - Feito, logo abaixo de `Debounce__MaxDispatchAttempts`, com o gatilho de
    recalibração e o fail-fast.
- [x] 5.6 `01-ARQUITETURA_E_CONVENCOES.md`, bloco do `PendingDispatch` (`:76` em
  `cb736db`; era `:72` em `18a2f3b`): o
  invariante "no máximo uma `Pending` e, salvo o teto, nenhum disparo novo com outra
  `Dispatching` da sessão". Grep por "Dispatching" no `01` e em `docs/`, corrigir o
  que afirmar disparo sem serialização, e registrar aqui o que foi achado, mesmo que
  nada (convenção 13).
  - Feito: parágrafo "Invariante da sessão" no bloco do `PendingDispatch` do `01`
    (`:81`).
  - **Grep por `Dispatching` no `01` e em `docs/`** (18/10/2026): nenhum trecho
    afirmava disparo sem serialização. Ocorrências: `01:102` e
    `docs/architecture.md` (`DispatchStatus` espelhado, inalterado);
    `docs/configuration.md` (as chaves da reconciliação, corretas); e os trechos
    escritos por esta change.
  - **Achado no caminho:** `docs/a2a-integration.md:426` diz que o histórico é
    limitado a 20 mensagens, e o código usa 200 mais a compactação. Anterior a esta
    change e fora dela: **#173** (6.4).
- [x] 5.7 `CHANGELOG.md`, em `[Unreleased]`: `Fixed`. A segunda mensagem de uma
  conversa ocupada sai depois da primeira, em lote, e não falha mais no lock.
  - Feito, primeiro item de `### Fixed`.
- [x] 5.8 `python3 scripts/check-docs.py` verde.
  - "Integridade da documentação: OK", depois da última edição do `02`.

## 6. Fechamento da implementação

- [x] 6.1 Suítes contra a baseline da 1.1, por classe, com as condições da VM:
  `apps/inbox`, `apps/workers` (tem de repetir o número da 1.1) e
  `InboxOrchestratorRoundTrip`.
  - **18/10/2026, sobre a árvore final** (as três em sequência, `postgres` e
    `rabbitmq` do compose de pé antes e depois, **zero** `vstest`/`testhost` de outras
    worktrees em todas as amostras):
    - `apps/inbox`: **252/252**, 40 s (load 3,06 → 6,04). Contra a 1.1: +4
      `DebounceSessionSerializationTests`, +7 `DebounceOptionsValidationTests`; as
      outras 31 classes com as mesmas contagens.
    - `apps/workers`: **405/405**, 5m17s (load 6,04 → 3,31). **Repete a 1.1**: as 42
      classes com as mesmas contagens (`diff` vazio).
    - `InboxOrchestratorRoundTrip`: **11/11**, 18 s (load 3,31 → 4,94). +1
      `SessionSerializationRoundTripTests`; os 10 da 1.1 iguais.
- [x] 6.2 Segunda perna de cada guarda (convenção 15), por guarda:
  - guardas 1 e 4: tirar o predicado da D1;
  - guarda 2: tirar o teto da D3;
  - guarda 3: trocar o filtro por um global.

  Em cada caso, ver reprovar e restaurar, registrando o motivo e o tempo.

  Feito em 18/10/2026. Cada reversão foi aplicada por substituição exata (falha se o
  trecho não existir uma vez), a classe dos guardas rodada, e o arquivo restaurado de
  backup com o checksum conferido igual ao de antes:
  - **sem o predicado da D1** (o método devolve os candidatos sem filtro):
    - inbox, 19 s: guarda 1 reprova com `["msg1", "msg2", "msg3"]`, e o par do guarda
      2 (`DispatchingWithinMaxWait_StillHoldsExpiredPending`) com `["pendente"]`;
      os guardas 3 e "além do teto" passam, como devem;
    - round-trip, 37 s: guarda 4 reprova com
      `[COMPLETED, FAILED, "aviso de falha", resposta]`.
  - **sem o teto da D3** (tirado `LastMessageAt > corte`), 25 s: só
    `DispatchingOlderThanMaxWait_NoLongerHoldsExpiredPending_AndIsLeftUntouched`
    reprova, `TimeoutException`: a linha além do teto passou a segurar a `Pending`.
  - **filtro global** (tirado `SessionId == candidate.SessionId`), 31 s: guarda 3
    reprova (`TimeoutException`, a sessão B esperou a A), junto com o guarda 1 e o
    "além do teto", que também ficam presos em linhas `Dispatching` de outras
    sessões da fixture.
  - **A mais, o registro da checagem de boot** (tirada a chamada do `Program.cs`),
    8 s: `InvalidMaxWait_FailsTheRealApplicationStartup` reprova
    (`No exception was thrown`). Os outros 6 da classe passam.
- [x] 6.3 `openspec validate inbox-serializacao-por-sessao --strict` verde.
  - "Change 'inbox-serializacao-por-sessao' is valid", em 18/10/2026, depois da última edição do `tasks.md`.
- [x] 6.4 Achado novo durante o apply vira issue na hora (convenção 23), com os
  rótulos da taxonomia, e é listado aqui.
  - **#173** (`tipo: docs`, `app: workers`): `docs/a2a-integration.md:426` diz que o
    histórico do `contextId` é limitado a 20 mensagens; o código usa 200
    (`AgentExecutionService.cs:58`) e compactação a cada 10 turnos. Na entrada do
    `02`.
- [x] 6.5 Parar e relatar. Sem commit, push, PR, sync nem archive: o dono decide
  cada um.

## 7. Depois da revisão do dono (não executar sem pedido)

- [x] 7.1 `/opsx:archive` com sync, feito pelo dono, depois do grupo 5.
  - Feito em 18/10/2026, a pedido do dono depois da revisão aprovada: em
    `inbox-message-orchestration`, 1 requisito acrescentado ("Disparo do debounce é
    serializado por sessão") e 1 alterado ("Debounce agrupa mensagens dentro da
    janela configurada", cenário "Mensagens fora da janela"). A 7.2 vem logo depois
    do archive, e a 7.3 é do dono.
- [ ] 7.2 Push, PR com `Closes #162`, e o cartão da #162 para `In review` logo
  depois do `gh pr create`.
- [ ] 7.3 Deploy só de `apps/inbox`, sem `migrator` (Migration Plan do `design.md`).
