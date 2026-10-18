## Context

**Origem.** A exploração `workers-consumo-concorrente` (17/10/2026) foi dividida em
duas changes. Esta corrige, do lado do inbox, a disputa pelo lock de contexto que a
outra tornaria frequente. Não depende dela no código e precisa chegar primeiro à
`main` (#162).

**O caminho de hoje, verificado no código.**

```
contato ──msg1──►  PendingDispatch P1 (Pending)
                     │  janela de 10 s vence, sweep (2 s) reivindica
                     ▼
                   P1 Dispatching ── SendMessage ──► apps/api ──► agent-tasks ──► worker A
                                                                                   │ StartWork → working
contato ──msg2──►  P1 não é Pending → cria P2 (Pending)                            │ pg_advisory_lock(agente, ctx)
                     │  10 s depois, sweep reivindica P2                            │ ... LLM, tools, delegação ...
                     ▼                    (nada olha para P1)
                   P2 Dispatching ── SendMessage ──► apps/api ──► agent-tasks ──► worker B
                                                                                   │ StartWork → working
                                                                                   │ pg_advisory_lock → ESPERA
                                                                                   │ 30 s (CommandTimeout)
                                                                                   ▼
                                                                    failed → push → "Não consegui responder agora"
```

- `PendingDispatch` tem índice único em `SessionId` filtrado por
  `Status = 'Pending'` (`AppDbContext.cs:126-128`). A sessão tem no máximo **uma**
  `Pending`, e **qualquer número** de `Dispatching`.
- `InboundMessageOrchestrator` só anexa a uma `Pending` (`FindPendingAsync`,
  `:147-149`). Com a anterior em `Dispatching`, cria outra (`:59`).
- `DebounceSweepService.ProcessDueDispatchesAsync` seleciona por `Status == Pending`
  e `LastMessageAt <= cutoff` (`:78-81`). `TryDispatchAsync` relê a linha como
  `Pending` (`:133-136`) e reivindica sob `xmin` (`:146-151`).
- O lock de contexto é por `(agentId, contextId)`, e o `contextId` é o da `Session`
  (`Session.cs:14,30`). Toda mensagem da sessão vira uma task nova no mesmo par.
- Com uma réplica de `apps/workers` (`prefetchCount: 1`), a task de P2 esperava na
  **fila** do RabbitMQ, sem prazo e em ordem. Com duas, que é como o piloto roda, ela
  espera o **lock**, com prazo de 30 s.
  `TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState` mede isso:
  `[Completed, Failed]`.

**Quem encerra uma linha em `Dispatching` hoje** (`docs/architecture.md`, #47):

- o push de `apps/workers`;
- a reconciliação, a cada 1 min (`DispatchReconciliationOptions.Interval`). Com
  `TaskId`, ela resolve depois do estado terminal + carência de 2 min
  (`TerminalGrace`). Sem `TaskId`, encerra como perda quando `LastMessageAt` tem
  mais de 10 min (`UntrackedDispatchMaxAge`);
- os desfechos de falha do próprio sweep: rejeição síncrona, rejeição de protocolo
  e esgotamento de tentativas.

**O que nenhum deles encerra:** task que nunca chega a estado terminal.
`AgentExecutionService.ExecuteAsync` devolve **sem** estado terminal quando o agente
não é encontrado (`:102-106`) ou quando a task não é encontrada depois das
retentativas (`:123-127`). Uma exceção que escapa de `ExecuteAsync` cai no `nack(requeue:
false)` do consumidor com a task em `Submitted` ou `Working`. O
`workers-nonterminal-task-detection` só observa. A #47 deixou isso fora de escopo de
propósito (non-goal "task que nunca termina"): ali, o custo era aquela mensagem. Com
a serialização, o custo passaria a ser **a conversa inteira** (D3).

## Goals / Non-Goals

**Goals:**

- Duas mensagens da mesma sessão nunca têm tasks em execução ao mesmo tempo **por
  causa do inbox**, com qualquer número de réplicas de `apps/inbox` e `apps/workers`.
- O que chega enquanto a sessão está ocupada sai num lote só, na ordem de chegada.
- Nenhuma sessão fica bloqueada sem limite: toda espera tem teto escrito.
- Custo escrito em números, e os dois achados registrados no `02` com gatilho e
  posição.

**Non-Goals:**

- Cliente A2A externo (D2).
- `apps/workers`: o lock, os 30 s, a ordem lock × `StartWorkAsync`, #132, #134.
- Concorrência dentro do processo de `apps/workers` (`workers-consumo-concorrente`).
- Resolver a colisão "falha de transporte × `Pending` nova" (Risks; #164).
- Fazer a reconciliação agir sobre task que nunca termina. D3 limita o efeito na
  sessão, não recupera a mensagem.

## Decisions

### D1 — O sweep não despacha uma sessão que tem linha em `Dispatching`

**Onde.** Nos dois pontos em que o sweep decide:

1. **Consulta de candidatos** (`ProcessDueDispatchesAsync`): `Pending`, janela
   vencida, e **não existe** outra `PendingDispatch` da mesma `SessionId` em
   `Dispatching` (respeitado o teto da D3). É um `NOT EXISTS` correlacionado na
   mesma consulta, sem ida e volta a mais.
2. **Releitura antes da reivindicação** (`TryDispatchAsync`): o mesmo predicado.
   Entre a consulta e a releitura passa no máximo um ciclo inteiro de candidatos em
   série (até 5 s cada), e a linha em `Dispatching` pode ter sido criada nesse meio.

**Por que a dupla checagem basta, sem lock nem índice novo.** Para uma `Pending` P2
ser reivindicada com outra linha da sessão em `Dispatching`, essa outra precisaria
**virar** `Dispatching` entre a checagem e o `SaveChanges` do claim de P2. Só vira
`Dispatching` uma linha `Pending` reivindicada pelo sweep, e o índice único garante
que a única `Pending` da sessão é a própria P2. A reconciliação e o endpoint de push
só **removem** linhas. A falha de transporte devolve a `Pending`, e nunca a
`Dispatching`. O claim de P2 continua sob `xmin`, como hoje: duas instâncias de
inbox que leem P2 ao mesmo tempo continuam disparando uma vez só (requisito
"Disparo do debounce é idempotente entre múltiplas instâncias").

**O lote.** Nada muda na ingestão. Enquanto P2 está bloqueada, ela continua
`Pending`, e `InboundMessageOrchestrator` continua anexando a ela. Quando a linha em
`Dispatching` some, P2 já tem a janela vencida, porque `LastMessageAt` é da última
mensagem. Ela sai no próximo ciclo, até 2 s depois, com `ConcatenatedText()` na
ordem de chegada. Se uma mensagem chegar exatamente nesse instante, a janela de
debounce volta a valer, como para qualquer `Pending`.

**Status `Failed` não bloqueia.** `MarkFailed` é sempre seguido de
`DispatchOutcomeProcessor.FailAsync`, que remove a linha. Uma `Failed` que
sobrevivesse seria resíduo de gravação, e não deve prender a conversa.

**Alternativas recusadas** (todas da exploração, decididas pelo dono):

| alternativa | por que não |
|---|---|
| aceitar a falha em 30 s | regressão visível ao usuário assim que houver mais de uma execução por vez, que já é o caso do piloto |
| aumentar a espera do lock no worker | o slot de worker fica preso esperando, e a posse do lock não tem teto: até 120 s de delegação por chamada, até 400 s de resposta travada (#134). Só muda onde o limite estoura |
| `pg_try_advisory_lock` + reentrega atrasada (fila de espera com TTL e DLX) | perde a ordem entre as mensagens da conversa, cria topologia nova no broker, e exige teto de voltas e reordenar o fluxo de métricas no worker |

### D2 — Cliente A2A externo continua com o contrato atual

Quem fala A2A direto com `apps/api` e manda duas mensagens concorrentes no mesmo
contexto continua podendo ter a segunda em `failed` depois de 30 s de espera pelo
lock. `apps/workers` não muda. Fica documentado em `docs/architecture.md`, na seção
do lock de contexto, como contrato e não como defeito: serializar para clientes
externos seria papel de `apps/api` ou do worker, e as duas formas são as
alternativas recusadas acima.

### D3 — A espera tem teto: 10 min contados de `LastMessageAt` da linha em `Dispatching`

**O problema que obriga o teto.** Sem ele, uma linha em `Dispatching` cuja task
nunca termina (Context, último bloco) bloquearia a sessão **para sempre**: toda
mensagem seguinte do contato ficaria em `Pending` sem disparo e sem aviso. Hoje
esse caso perde uma mensagem. Sem o teto, perderia a conversa.

**A regra.** Uma linha em `Dispatching` com `LastMessageAt` mais velho que o teto
**deixa de bloquear** a sessão. A linha não é alterada; ela continua sendo da
reconciliação. Passado o teto, a sessão volta ao comportamento de hoje: a `Pending`
sai, e se a execução antiga ainda segurar o lock, o worker aplica os 30 s dele.

**Por que `LastMessageAt`, e não um carimbo de reivindicação.** A linha não guarda
o instante do claim, e acrescentar exigiria migration. O claim acontece no mínimo
`Window` (10 s) depois de `LastMessageAt`. A idade medida por `LastMessageAt` é então
um **limite superior** da idade do disparo: o teto real fica entre 9 min 50 s e 10
min. A diferença não muda nada que este teto protege.

**Por que 10 min.**

- **O que o número cobre de fato:** **uma** chamada travada da #134 (até 400 s,
  6 min 40 s, no OpenAI: 4 × 100 s) mais **uma** delegação (até 120 s). São 8 min 40
  s, e sobra folga.
- **O que pode passar dele, e passa:**
  - turnos com **várias** chamadas travadas, ou várias delegações em sequência;
  - a **espera na fila do RabbitMQ** antes de a execução começar. O teto conta de
    `LastMessageAt`, e não do início da execução, então o tempo de fila consome o
    mesmo orçamento.
- **Por que isso é aceitável, e é essa propriedade que sustenta a D3:** passar do
  teto **devolve o comportamento de hoje**, e nunca algo pior. A `Pending` sai, e
  se a execução antiga ainda segurar o lock, o worker aplica os 30 s dele e a
  segunda task termina `failed` com o aviso, exatamente como antes desta change.
  O teto não cria modo de falha novo. Ele só define até quando a serialização
  protege.
- **É o mesmo valor de `UntrackedDispatchMaxAge`.** Com o mesmo número, a linha sem
  `TaskId` deixa de bloquear praticamente no mesmo momento em que a reconciliação a
  encerra, e o operador lê um número só.
- **Não foi medido** como duração real de turno: a medição que existe é a do pior
  caso de rede (#134), não a distribuição.
  - **Gatilho de recalibração (convenção 22):** a `task_executions` mostrar turnos
    acima de 5 min, ou a #134 mudar o teto de resposta travada, ou
    `UntrackedDispatchMaxAge` mudar.

**Configuração.** `Debounce:SessionSerializationMaxWait`, em `DebounceOptions`, com
default de 10 min no tipo. É configurável pelo mesmo motivo de `Window` e
`SweepInterval`: os testes precisam de valores curtos, e o default é o de produção
(convenção 2, mesmo raciocínio de `DispatchReconciliationOptions`).

Validado no boot (convenção 8). Dois casos **reprovam**, pelo mesmo argumento, e
nenhum é corrigido automaticamente:

- **teto menor ou igual a zero:** nenhuma linha em `Dispatching` bloquearia;
- **teto menor ou igual a `Debounce:Window`:** o claim só acontece com
  `LastMessageAt` mais velho que `Window`. Toda linha em `Dispatching` já nasceria
  além do teto, e também não bloquearia nada.

Nos dois casos a serialização ficaria desligada **em silêncio**, e isso é o defeito
de volta. A mensagem de erro nomeia a chave, o valor recebido e, no segundo caso, o
`Window` vigente.

**Alternativas recusadas:**

- **Sem teto:** a conversa morre em silêncio por um caso que o worker já produz.
- **Teto em `TerminalGrace` + `Interval` (3 min):** fica abaixo da resposta travada
  legítima da #134. Liberaria a sessão com a primeira ainda segurando o lock, e
  traria de volta exatamente a falha que esta change corrige.
- **Fazer a reconciliação encerrar task não-terminal envelhecida:** encerrar a linha
  não para a task no worker. Se ela terminasse depois, a resposta chegaria a uma
  linha que não existe mais. É decisão da `workers-nonterminal-task-detection`, que
  hoje só observa.

### D4 — O teste `[Completed, Failed]` do worker fica, com nota; o gêmeo de ida e volta é o guarda

`TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState`
(`ConversationContextLockFailureTests.cs:143`) **continua verdadeiro**: no nível do
worker, duas tasks concorrentes no mesmo contexto ainda produzem
`[Completed, Failed]`, e isso é o contrato da D2. O propósito dele também continua
válido: provar que **nenhuma** das duas fica presa em `working` (change
`lock-de-contexto-falha-terminal`).

- **Fica como está**, com uma nota no XML doc: o inbox não produz mais esse cenário
  desde esta change, e o que ainda o produz é cliente A2A externo (D2). Sem a nota,
  quem lê `[Completed, Failed]` conclui que a conversa do contato falha, o que deixa
  de ser verdade (convenção 13, forma temporal).
- **Ganha um gêmeo de ida e volta** em `tests/InboxOrchestratorRoundTrip.Tests`, com
  os três apps reais, duas instâncias de worker e LLM lento. É esse o guarda de que o
  caminho do inbox não produz mais a falha (D5).

**Alternativa recusada:** reescrever o teste do worker para `[Completed, Completed]`.
O worker não muda, e o teste passaria a afirmar uma coisa falsa sobre ele.

### D5 — Guardas (convenção 15)

Cada guarda é verificado contra o defeito antes de valer: remove-se o predicado da
D1 (ou o teto da D3), vê-se o guarda reprovar, e só então a correção volta.

1. **Determinístico, no inbox** (`Buteco.Inbox.Tests`, fake A2A que responde
   `Submitted`, então a primeira linha fica em `Dispatching` sem push):
   - msg1 sai; msg2 e msg3 chegam fora da janela;
   - passados `Window` + 2 × `SweepInterval`, **um** `SendMessage` só para o
     contexto, e a `Pending` com as duas mensagens;
   - resolve-se a primeira pelo endpoint de push real (token, task `Completed`);
   - sai o segundo `SendMessage`, **um**, com texto `"msg2\nmsg3"`.

   Contra o defeito, msg2 sai sozinha antes da resolução. A ausência por tempo é a
   asserção fraca. O par determinístico é o **conteúdo do lote**: as duas mensagens
   num disparo só (convenção 15, quinta forma).
2. **Teto (D3), no inbox:** com o teto curto, uma linha em `Dispatching` mais velha
   que ele **não** bloqueia a `Pending` da sessão. O par é uma mais nova que o teto,
   que bloqueia. Contra a remoção do teto, o primeiro reprova.
3. **Outra sessão não é afetada:** com a sessão A em `Dispatching`, a `Pending`
   vencida da sessão B sai normalmente. Prende o `NOT EXISTS` à mesma `SessionId`:
   um filtro global passaria nos guardas 1 e 2 e serializaria o inbox inteiro.
4. **Ida e volta** (`tests/InboxOrchestratorRoundTrip.Tests`): duas instâncias de
   worker, `Command Timeout` curto (o mesmo recurso de
   `ConversationContextLockFailureTests`), LLM mais lento que esse timeout. msg1,
   espera a task em `Working`, msg2. Com a correção: as duas tasks `Completed`, duas
   respostas entregues, nenhum aviso de falha. Contra o defeito: `[Completed, Failed]`
   e o aviso entregue.
   - A segunda instância sobe só depois de a primeira task estar em `Working`. É a
     mesma precaução de `ConversationContextLockFailureTests.cs:167-172`: senão as
     duas tasks podem cair na mesma instância, serializar na fila, e o guarda passa
     verde sem exercitar nada.
5. **Teste que muda de propósito:** `MessagesOutsideWindow_TriggerSeparateSendMessageCalls`
   (`DebounceSweepServiceTests.cs:95`) espera o segundo `SendMessage` com o primeiro
   em `Dispatching`, ou seja, afirma o defeito. Passa a resolver o primeiro disparo
   antes. Verificado: com a correção e sem o ajuste, ele reprova. É a prova de que o
   ajuste é necessário, e não cosmético.

A fixture do round-trip ganha os controles do guarda 4 (atraso do LLM, `Command
Timeout` do worker, segunda instância), **neutros por padrão**. É o mesmo padrão de
`PushGate` e `ChatClientFails`, da #47.

### D6 — Nenhuma garantia no banco

Um índice único parcial em `SessionId` filtrado por `Status = 'Dispatching'` faria o
Postgres recusar o segundo claim.

**Recusado:**

- O invariante já vale por construção (D1).
- O índice exigiria migration, e a migration **falharia** em banco que já tem
  sessões com duas linhas em `Dispatching`, que é exatamente o estado que o defeito
  produz no piloto.
- O índice também não conviveria com a D3: a linha antiga continua em `Dispatching`
  depois do teto, e a nova não conseguiria ser reivindicada.

### Árvore de arquivos

```
apps/inbox/
├── src/Buteco.Inbox/
│   ├── Orchestration/
│   │   ├── DebounceOptions.cs                  (+ SessionSerializationMaxWait, D3)
│   │   ├── DebounceOptionsValidation.cs        (novo: checagem no boot, D3; convenção 8)
│   │   └── DebounceSweepService.cs             (consulta de candidatos e releitura, D1/D3)
│   └── Program.cs                              (chama a checagem no boot)
└── tests/Buteco.Inbox.Tests/
    ├── DebounceSessionSerializationTests.cs    (novo: guardas 1, 2 e 3)
    ├── DebounceOptionsValidationTests.cs       (novo: teto <= 0 e teto <= Window reprovam o boot)
    └── DebounceSweepServiceTests.cs            (guarda 5: ajuste de propósito)

apps/workers/tests/Buteco.Workers.Tests/
└── ConversationContextLockFailureTests.cs      (só a nota da D4)

tests/InboxOrchestratorRoundTrip.Tests/
├── SessionSerializationRoundTripTests.cs       (novo: guarda 4)
└── Support/RoundTripFixture.cs                 (controles neutros por padrão)

docs/architecture.md                            (ciclo de vida do PendingDispatch; contrato da D2)
docs/configuration.md                           (Debounce__SessionSerializationMaxWait)
01-ARQUITETURA_E_CONVENCOES.md                  (PendingDispatch: serialização por sessão)
02-HISTORICO_E_STATUS.md                        (histórico; dois itens com gatilho)
CHANGELOG.md
```

Nada em `libs/`. Nada em `apps/api`, `apps/connectors` nem `apps/frontend`.

## Risks / Trade-offs

- **[Execução longa e legítima bloqueia a sessão]** Delegação de até 120 s por
  chamada, resposta travada de até 400 s (#134). → Aceito: é a espera que a fila já
  impunha com uma réplica, e o que chega nesse tempo sai num lote só, em vez de virar
  N tasks. O teto da D3 fica acima disso.
- **[Linha presa bloqueia a sessão]** Com `TaskId` e push perdido: até ~3 min depois
  do estado terminal (2 min de carência + 1 min de intervalo). Sem `TaskId`: até ~11
  min (10 min de idade + 1 intervalo), e a D3 libera a sessão um pouco antes. → A
  reconciliação já limita os dois casos. O caso sem `TaskId` vira item no `02` com
  gatilho e posição (tarefa 5.2).
- **[Task que nunca termina]** Sem a D3, a sessão ficaria bloqueada para sempre. →
  D3; o guarda 2 cobre.
- **[Colisão entre falha de transporte e `Pending` nova]** **Defeito anterior a esta
  change, que ela agrava.** P1 em `Dispatching`, com o `SendMessage` em voo (até 5 s);
  chega msg2 e cria P2 `Pending`; o `SendMessage` de P1 falha por transporte, e
  `RegisterTransportFailure` tenta devolver P1 a `Pending`. O índice único recusa
  (`23505`), a exceção não é tratada em `HandleTransportFailureAsync`, e o `catch`
  por candidato só loga. P1 fica em `Dispatching` sem `TaskId` até a reconciliação a
  encerrar como perda (~11 min).
  - **Hoje:** perde-se msg1, e msg2 sai normalmente.
  - **Com esta change:** perde-se msg1, e msg2 espera até o teto da D3.
  - Verificado por leitura de código, não reproduzido. É janela de 5 s, só com falha
    de transporte. → **Fora do escopo**: **#164** (convenção 23), na fila logo
    depois da #162, sem bloquear o merge dela. A correção natural seria anexar as
    mensagens de P1 à frente de P2 e remover P1, mas é decisão da issue.
- **[A ausência do segundo disparo é medida por tempo]** → Pareada com a asserção do
  conteúdo do lote (D5, guarda 1).
- **[Cliente A2A externo]** Continua podendo falhar em 30 s. → D2, documentado.
- **[Latência da segunda resposta]** Mais até um `SweepInterval` (2 s) depois da
  resolução da primeira. → Desprezível diante da execução.

## Migration Plan

- **Sem migration.** O predicado usa `SessionId` e `Status`, que já existem.
- **Deploy:** só `apps/inbox`, pelo runbook normal de redeploy, sem o passo do
  `migrator`. Pode subir antes de `workers-consumo-concorrente` e independe dela.
- **Estado existente no piloto:** sessões que já tiverem linhas em `Dispatching`
  passam a segurar a `Pending` dessas sessões até as linhas serem resolvidas, ou até o
  teto da D3. Nenhuma limpeza manual é necessária: a reconciliação e o teto liberam
  sozinhos.
- **Rollback:** reimplantar a imagem anterior de `apps/inbox`. Sem dado novo a
  desfazer. A chave `Debounce__SessionSerializationMaxWait`, se tiver sido definida,
  é ignorada pela versão anterior.

## Open Questions

Nenhuma. A colisão "falha de transporte × `Pending` nova", que estava aqui, virou a
**#164** (Risks).
