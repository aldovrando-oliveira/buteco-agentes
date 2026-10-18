**Issue:** #162

## Why

O inbox despacha a segunda mensagem de uma sessão enquanto a primeira ainda está em
`Dispatching`. As duas viram tasks no mesmo `(agentId, contextId)`, e a segunda
espera o `pg_advisory_lock` de contexto em `apps/workers`. Se a primeira segura o
lock por mais de 30 s (`CommandTimeout` do Npgsql), a segunda termina `failed` e o
contato recebe *«Não consegui responder agora»*.

O defeito **já existe no piloto**, que roda com duas réplicas de `apps/workers`, e
um teste o afirma como comportamento esperado:
`TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState` espera
`[Completed, Failed]` (`ConversationContextLockFailureTests.cs:143`). Com uma
réplica só, a segunda task esperava na fila do RabbitMQ, sem prazo e em ordem, e o
defeito não aparecia.

O mecanismo, verificado no código:

- o índice único de `PendingDispatch` filtra só `Status = 'Pending'`
  (`apps/inbox/src/Buteco.Inbox/Infrastructure/AppDbContext.cs:126-128`). Uma linha
  em `Dispatching` não impede uma `Pending` nova da mesma sessão
  (`InboundMessageOrchestrator.cs:52-61`);
- o `DebounceSweepService` seleciona candidatos por `Status == Pending` e janela
  vencida, sem excluir a sessão que já tem linha em `Dispatching`
  (`DebounceSweepService.cs:78-81`).

**Por que agora.** A change `workers-consumo-concorrente` vai pôr várias execuções
em paralelo dentro de um processo só. Isso torna o defeito frequente e acaba com o
único arranjo em que ele não acontecia (uma réplica). Esta change é **pré-requisito
de merge** daquela. As duas podem ser desenvolvidas em paralelo, mas esta chega
primeiro à `main`.

## What Changes

- **`apps/inbox`, o disparo passa a ser serializado por sessão.** O
  `DebounceSweepService` não despacha uma `Pending` enquanto a mesma sessão tiver
  uma linha em `Dispatching`, tanto na consulta de candidatos quanto na
  reivindicação. As mensagens que chegam nesse meio-tempo se acumulam na `Pending`,
  como já acontece dentro da janela de debounce, e saem **num lote só** no primeiro
  ciclo de varredura depois de a linha em `Dispatching` ser resolvida (push,
  reconciliação ou desfecho de falha).
  - Preserva a ordem das mensagens da conversa.
  - Não ocupa slot de worker esperando lock.
  - Funciona com qualquer número de réplicas de `apps/inbox` e de `apps/workers`.
- **O custo da serialização fica escrito, com os números.** A sessão também fica
  bloqueada:
  - durante execuções longas e legítimas: delegação de até 120 s por chamada, e
    resposta travada do provedor de até 400 s (#134);
  - enquanto uma linha presa espera a reconciliação: ~3 min depois do estado
    terminal com `TaskId` (carência de 2 min + intervalo de 1 min), e até ~11 min
    sem `TaskId` (10 min de idade + até 1 intervalo).

  É aceitável porque equivale à espera na fila que uma réplica única já impõe hoje,
  com uma diferença a favor: as mensagens acumuladas saem juntas, num disparo só.
- **Guarda novo (convenção 15).** Duas mensagens da mesma sessão, com a primeira em
  `Dispatching`, saem como despachos serializados e terminam as duas `Completed`.
  Tem duas formas: uma determinística no inbox (sem segundo `SendMessage` enquanto a
  primeira está em `Dispatching`) e uma de ida e volta com os três apps reais e duas
  instâncias de worker (sem a correção, `[Completed, Failed]`).
- **Teste que muda de propósito:** `MessagesOutsideWindow_TriggerSeparateSendMessageCalls`
  (`DebounceSweepServiceTests.cs:95`) afirma o defeito: o segundo `SendMessage` sai
  com o primeiro ainda em `Dispatching`. Ele passa a resolver o primeiro disparo
  antes de esperar o segundo.
- **Registros no `02`, com gatilho e posição:**
  - linha presa em `Dispatching` sem `TaskId` bloqueia a sessão por até ~11 min;
  - achado lateral em `apps/workers`: o lock de contexto é adquirido **depois** de
    `StartWorkAsync` (`AgentExecutionService.cs:245` contra `:283`). A espera pelo
    lock aparece como `working` e contamina as métricas de tempo de fila e de lock.
    Não é escopo desta change.

**Fora de escopo, explícito:**

- **Cliente A2A externo** (fora do inbox): mensagens concorrentes no mesmo contexto
  continuam podendo falhar em 30 s. É o contrato atual, documentado, sem mudança.
  `apps/workers` não muda.
- O valor de 30 s do lock, a ordem lock × `StartWorkAsync`, e as #132 e #134.
- Concorrência dentro do processo de `apps/workers` (`workers-consumo-concorrente`).
- A colisão entre a falha de transporte de um disparo e uma `Pending` nova da mesma
  sessão (ver Risks no `design.md`). É defeito anterior a esta change, que esta
  change agrava: **#164**, na fila logo depois desta, sem bloquear o merge dela.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `inbox-message-orchestration`:
  - requisito novo: o disparo do debounce é serializado por sessão;
  - "Debounce agrupa mensagens dentro da janela configurada": o cenário "Mensagens
    fora da janela geram disparos separados" passa a exigir que o segundo disparo
    só aconteça depois de o primeiro ciclo ser resolvido.

## Impact

- **Código (só `apps/inbox`):** `Orchestration/DebounceSweepService.cs` (consulta de
  candidatos e reivindicação). Nenhuma migration: o filtro novo usa colunas que já
  existem (`SessionId`, `Status`). Nada em `apps/api`, `apps/workers`,
  `apps/connectors`, `apps/frontend` nem `libs/`.
- **Testes:**
  - `apps/inbox/tests/Buteco.Inbox.Tests`: guarda determinístico novo, e
    `DebounceSweepServiceTests` ajustado;
  - `tests/InboxOrchestratorRoundTrip.Tests`: guarda de ida e volta com duas
    instâncias de worker;
  - `apps/workers/tests/.../ConversationContextLockFailureTests.cs`: só uma nota no
    teste `[Completed, Failed]`, que continua verdadeiro no nível do worker (D3 do
    `design.md`).
- **Documentação:** ciclo de vida do `PendingDispatch` em `docs/architecture.md` e no
  `01-ARQUITETURA_E_CONVENCOES.md`; histórico e dois itens com gatilho no `02`.
- **Comportamento observável:**
  - a segunda mensagem de uma conversa ocupada fica "pendente" no painel até a
    primeira terminar, em vez de ir para "em processamento" e falhar;
  - mensagens enviadas enquanto o agente ainda responde saem juntas, num disparo só;
  - a latência da segunda resposta passa a incluir até um `SweepInterval` (2 s) depois
    da resolução da primeira.
- **Conflitos esperados com `workers-consumo-concorrente`:**
  `02-HISTORICO_E_STATUS.md`, `ConversationContextLockFailureTests.cs` e talvez
  `tests/InboxOrchestratorRoundTrip.Tests`. Quem mergear por último faz rebase.
