## Context

O ciclo de disparo de `apps/inbox` é uma linha de `pending_dispatches` que passa
por `Pending` → `Dispatching` → removida. Em `Dispatching` ela depende de um
único evento para sair: o push de `apps/workers`, recebido por
`PushNotificationEndpoints`, que acha a linha pelo `TaskId` (`:61`) e a remove
(`:96`). Nada mais lê linha em `Dispatching`.

**Varredura de escopo (convenção 22), 03/10/2026, sobre `cb74315`, conferida item
a item com `grep -n`:**

- *critério estreito, quem escreve `Dispatching`:* um lugar só,
  `DebounceSweepService.cs:100` (via `PendingDispatch.MarkDispatching`,
  `PendingDispatch.cs:57-59`), mais o espelho em `Message` na `:101`;
- *critério largo, quem lê ou escreve `pending_dispatches`:* 10 pontos em 4
  arquivos.
  - `DebounceSweepService.cs`: lê nas `:44` e `:87`; remove nas `:142`, `:241`
    e `:268`.
  - `InboundMessageOrchestrator.cs`: grava na `:60`, lê `Pending` na `:148`.
  - `PushNotificationEndpoints.cs`: lê pelo `TaskId` na `:61`, remove na `:96`.
  - `AppDbContext.cs:19,105-137`: mapeamento, índice parcial de `Pending`, índice
    de `TaskId` e `xmin`.

  Fora do backend, `MessageBubble.tsx:6` (`apps/frontend`) mostra "Processando"
  para o espelho em `Message`. `apps/workers` só cita a tabela num comentário
  (`AgentExecutionService.cs:266`).

**As quatro fontes**, reproduzidas por teste sobre o código atual (os testes
afirmam o defeito e viram os guardas desta change):

```
 inbox                       api / workers                      inbox
 MarkDispatching ──commit──▶ SendMessage ──▶ task ─▶ terminal ─▶ push ──▶ endpoint acha pelo TaskId
      │   ①                        │ ④                    │ ③       │ ②
      └─ para / exceção ───────────┘ push chega antes     │ parada  └─ rede falha (5 s, sem retentativa)
         TaskId fica NULO             do TaskId → 401     └─ reentrega → guarda terminal → sem push
```

1. **inbox para ou lança entre o claim e o `TaskId`.** A parada normal do host
   com o `SendMessage` em voo produz o estado de forma determinística: o
   `OperationCanceledException` com o token cancelado passa pelos filtros dos
   dois `catch` de `DebounceSweepService` (`:49`, `:70`). Uma exceção que não é
   de transporte (nem `A2AException`) depois do claim também. A linha fica com
   `TaskId` **nulo**. O `kill -9` em si não foi reproduzido; chega ao mesmo estado
   por outra porta. Na parada normal o `SendMessage` pode já ter chegado à
   `apps/api`: a task roda, o push chega com `TaskId` desconhecido (401), e a
   resposta do agente se perde.
2. **push que falha.** Round-trip com os três apps, webhook falhando: task
   `Completed` em `apps/api`, linha em `Dispatching` com `TaskId`.
3. **parada do worker entre o estado terminal e o push.** Round-trip, push segurado
   até a parada. Log: push cancelado → job devolvido → `Task … já está em estado
   terminal (Completed) — mensagem reentregue, não executada de novo` → nenhum
   push.
4. **push precoce (nova).** A task termina antes de a resposta do `SendMessage`
   voltar e o `TaskId` ser gravado (`HandleResponseAsync`, `:246-249`). O endpoint
   não acha a linha, responde 401, o worker só loga (`PushNotificationSender.cs:45-50`)
   e o `TaskId` é gravado em seguida.

**E o caminho de falha do worker envia push** (`AgentExecutionService.cs:554-568`;
medido: task `Failed`, push 200, linha removida). Os 7 de 7 `Failed` de 22/09 são a
fonte 2 com a causa da falha em comum: o push ia ao túnel do inbox por IPv6 sem
conectividade.

**O caminho normal, hoje, por desfecho** — a régua para "nada de semântica nova":

| desfecho | entrega | status das entradas |
|---|---|---|
| push, task `Completed` com texto | texto ao canal, saída `Sent`/`Failed` | `Completed` |
| push, task `Completed` sem texto | nada | `Completed` |
| push, task `Failed` | **nada** | **`Completed`** (`:94`, sem guarda) |
| rejeição síncrona / de protocolo | nada | `Failed` |
| esgotamento de tentativas | nada | `Failed` |

O dono decidiu (03/10/2026) que o aviso de falha **entra nesta change**, para todos
os desfechos de falha, com texto fixo, e que a task `Failed` passa a `Failed`.

**O que já existe e não serve:** o `NonTerminalTaskDetectorService` fica em
`apps/workers` (`Diagnostics/NonTerminalTaskDetectorService.cs:123`), varre
`a2a_tasks` em outro banco e só observa. O `DebounceSweepService` varre `Pending`
por idade (`:44-47`), a cada 2 s.

**O que a consulta pelo `TaskId` precisa já existe:** o `A2AClient` do pacote
`A2A 1.0.0-preview2` tem `GetTaskAsync`, e no servidor o `GetTask` devolve a
`AgentTask` inteira do store, com artifacts, ou `A2AException(TaskNotFound)`
(decompilado: `A2AServer`, linha 1339). O `service:inbox` já pode
`POST /agents/{id}/a2a` (`ServiceScopeAuthorizationHandler.cs:39`), e
`PostgresTaskStore.GetTaskAsync` de `apps/api` não filtra por agente (`:15-17`).
**Nenhuma mudança em `apps/api`.**

## Goals / Non-Goals

**Goals:**

- nenhuma linha em `Dispatching` sobrevive indefinidamente quando a task dela
  terminou (fontes 2, 3 e 4) ou quando ela nunca chegou a ter task registrada
  (fonte 1);
- a resolução é **a mesma** do push: o mesmo código decide entrega e status;
- todo desfecho em que nenhuma resposta virá avisa a conversa;
- entrega única entre instâncias de `apps/inbox` e entre push e reconciliação, com
  o resíduo nomeado.

**Non-Goals:**

- task que nunca termina (`Submitted` nunca consumida, `Working` presa): a linha
  fica como está; é da `workers-nonterminal-task-detection`;
- retentativa do push em `apps/workers` (D1);
- texto do aviso configurável;
- regra no código para as linhas já existentes (D9);
- tocar `apps/api`, `apps/workers`, `apps/frontend` ou `libs/`.

## Decisions

### D1 — Reconciliação no inbox (opção B+), sem retentativa do push

| opção | ① | ② | ③ | ④ |
|---|---|---|---|---|
| A — retentativa no `PushNotificationSender` | não | só falha passageira | não | sim, com backoff |
| A′ — A + reenvio na guarda terminal | não | passageira | sim | sim |
| B — reconciliação pelo `TaskId` | não | sim | sim | sim |
| **B+ — B + encerramento por idade sem `TaskId`** | **sim** | **sim** | **sim** | **sim** |

A fonte ① se divide depois de D11: a **parada normal** passa a gravar o `TaskId`
(D11), e só crash e exceção inesperada chegam a D7.

Escolhida pelo dono: **B+**. É a única que cobre as quatro, com um mecanismo só.
*Descartada, A + B+:* a retentativa só encurtaria a recuperação de falha passageira
(de minutos para segundos) e prenderia o consumidor do worker (prefetch 1) durante o
backoff. *Descartada, A′:* toca a guarda terminal da #49 e não cobre ①.

### D2 — Serviço próprio, não extensão do `DebounceSweepService`

`Orchestration/DispatchReconciliationService.cs`, `BackgroundService` com a forma do
`DebounceSweepService`: `PeriodicTimer`, escopo por ciclo, `try/catch` envolvendo a
consulta de candidatos e outro por linha (convenção 4, com a consulta **dentro** do
`try`).

*Descartada, estender o `DebounceSweepService`:* a cadência é outra (2 s contra
1 min, D8), cada linha faz uma chamada HTTP a `apps/api` com 5 s de timeout, e uma
lentidão da `apps/api` atrasaria o disparo de mensagens novas, que hoje não
depende dela entre um claim e outro. *Descartado, estender o
`NonTerminalTaskDetectorService`:* outro app, outro banco, e é instrumento de
observação, não de ação.

### D3 — Linha com `TaskId`: decide o estado terminal, com carência

Para cada linha em `Dispatching` com `TaskId`:
`IA2AClientFactory.CreateForAgent(channel.AgentId).GetTaskAsync(TaskId)`.

- **terminal (`IsTerminal()`, o mesmo predicado do worker) e
  `Status.Timestamp` mais velho que a carência** → reivindica (D4) e resolve pelo
  processamento do push (D5);
- **terminal sem `Status.Timestamp`** → a carência conta da **primeira vez que
  esta instância viu a task terminal**. O registro fica em memória, por `TaskId`, e
  as entradas de linhas que saíram de `Dispatching` são descartadas a cada ciclo.
  Nunca reconcilia na primeira observação (seria entrega em dobro com um push em
  voo) e nunca deixa de reconciliar (a linha ficaria órfã). Reinício da instância
  zera o registro: a espera recomeça, e a linha é reconciliada uma carência depois;
- **terminal dentro da carência** → nada neste ciclo;
- **não-terminal** → nada, sem log acima de `Debug` (é o caso normal de task em
  execução);
- **`TaskNotFound` ou falha da consulta** → nada, log `Warning` com `TaskId` e
  `SessionId`.

**A idade da linha não entra** (é a pergunta da issue sobre o problema dos 30 s):
uma linha em `Dispatching` é legítima enquanto a task roda, por quanto tempo for,
inclusive uma delegação de 120 s. O estado terminal em `apps/api` responde
"órfã?" sem limiar.

**O carimbo, medido.** Em 03/10/2026, no round-trip com os três apps, o JSON cru do
`GetTask` traz `status.timestamp` em task `Completed` e em task `Failed`
(`TerminalTask_CarriesStatusTimestamp_InRawPersistedJson`, 2/2). `Canceled` e
`Rejected`, lidos no SDK decompilado: os quatro métodos terminais do `TaskUpdater`
gravam `Timestamp = DateTimeOffset.UtcNow` (`CompleteAsync` :98, `FailAsync` :117,
`CancelAsync` :135, `RejectAsync` :153). Nenhum dos dois chega a uma linha com
`TaskId`: o `Rejected` síncrono remove a linha na hora (`HandleResponseAsync`), o
`Rejected` assíncrono só existe acima da profundidade 5 de delegação, sem push, e
nada no sistema chama `CancelTask`. O tipo do carimbo é anulável
(`DateTimeOffset?`), e uma task gravada por fora pode vir sem ele; daí a regra
acima.

**A carência existe por outro motivo:** não competir com um push que ainda está
sendo processado, porque o endpoint entrega ao canal **antes** de remover a linha
(`:88`, `:96`). Derivação, com o estado escrito ao lado (convenção 22):

- o push é disparado logo depois do `SaveTaskAsync` terminal e tem **5 s** de
  timeout (`apps/workers/Program.cs:67`);
- depois de aceito, o endpoint segue com `CancellationToken.None`, e a entrega ao
  canal usa o `HttpClient` anônimo dos senders (`TelegramOutboundMessageSender.cs:13`,
  `WahaOutboundMessageSender.cs:12`), com o timeout padrão de **100 s**;
- 5 s + 100 s + persistência → **carência de 2 min**, contada do `Status.Timestamp`
  terminal gravado pelo worker.

**Gatilho de recalibração:** qualquer mudança no timeout do push ou dos senders de
canal, ou um sender com timeout próprio. A mudança que mexer neles recalibra a
carência.

### D4 — Reivindicação por troca do `ExpectedToken`, sob `xmin`

Antes de entregar, a reconciliação troca o `ExpectedToken` da linha por um valor
novo e grava. A gravação é condicionada pelo `xmin` que a entidade já tem
(`AppDbContext.cs:137`).

- **duas instâncias:** a que perde recebe `DbUpdateConcurrencyException` e pula a
  linha;
- **push que chega depois da reivindicação:** o token não confere, e a resposta é
  401 (`PushNotificationEndpoints.cs:64`), sem entrega em dobro;
- **o valor muda sempre**, inclusive numa segunda reivindicação da mesma linha;
  então o `UPDATE` acontece de fato, e o `xmin` é conferido.

**Quem perde a corrida no fim (resíduo 1 de Risks).** O endpoint de push leu a
linha com o token válido e está entregando quando a reconciliação a reivindica
(push mais lento que a carência). O `SaveChanges` final do endpoint lança
`DbUpdateConcurrencyException`. Ele **não** vira 500 com log `Error`:

- o endpoint responde **200** (o desfecho foi processado, e o worker não reage à
  resposta, `PushNotificationSender.cs:45-50`);
- log **`Warning`** com `TaskId` e `SessionId`, dizendo que o desfecho já tinha sido
  resolvido pela reconciliação e que a entrega pode ter acontecido em dobro. É o
  único rastro do resíduo, e é raro por construção (exige passar da carência);
- o registro da mensagem de saída **deste** lado se perde junto com o `SaveChanges`
  revertido. O que fica persistido é o da reconciliação.

A reconciliação que perde a reivindicação (`DbUpdateConcurrencyException` no
claim) pula a linha, com log `Debug`: é o caminho esperado da concorrência, como no
claim do `DebounceSweepService` (`:107-113`).

**Guarda (barato):** no processador, com dois `DbContext`. O primeiro carrega a
linha, o segundo a reivindica e grava, e o primeiro termina o desfecho. Afirma que
nada propaga, que o resultado é "já resolvido", e que o log é `Warning`. O
endpoint só traduz esse resultado em 200.

*Descartado, coluna nova de estado ("Reconciling"):* exige migration e cria um
estado em que a linha pode ficar presa, que é o defeito desta change.

**DIVERGÊNCIA DA IMPLEMENTAÇÃO, 04/10/2026 (convenção 9), aprovada pelo dono no
momento:** a troca do token sob `xmin` **não basta**. Ela exclui só quem leu a
**mesma versão** da linha. A instância A reivindica, grava e fica entregando; a
instância B lê a linha **depois** dessa gravação, vê uma versão nova ainda em
`Dispatching`, reivindica de novo sem conflito e entrega também. Medido: o guarda
de duas instâncias, tornado determinístico na segunda perna (o sender segura a
primeira entrega), deu **2 entregas com a correção como desenhada**. Em produção,
basta a entrega de A cruzar o ciclo de B, e os senders têm até 100 s.

**O que entrou:** coluna anulável `ReconciliationClaimedAt` em
`pending_dispatches` (migration `AddReconciliationClaimedAt`), gravada na mesma
reivindicação, sob o mesmo `xmin`. Enquanto ela for mais nova que o **prazo de
posse** (`ClaimLease`, 2 min, mesma derivação da carência: 100 s do sender +
gravação), nenhuma instância reivindica a linha de novo. Ela é conferida na lista
de candidatos e relida na reivindicação, porque o `GetTask` fica entre as duas.
Vencido o prazo com a linha ainda em `Dispatching`, quem a reivindicou morreu ou
foi parado no meio da entrega, e ela volta a ser reivindicável (resíduo 2). **Não
cria estado preso**, que era o motivo da recusa acima: a posse expira.
*Descartados:* marcar a reivindicação no próprio `ExpectedToken` (mesmo efeito, mas
sobrecarrega a coluna de token) e aceitar o dobro como resíduo.
*Descartado, só a carência sem reivindicação:* não protege duas instâncias de
`apps/inbox` uma da outra.

### D5 — Um processamento de desfecho para o push e a reconciliação

`Orchestration/DispatchOutcomeProcessor.cs` (scoped) recebe a linha e a
`AgentTask` e decide tudo o que o endpoint decide hoje:

| `AgentTask` | entrega | status das entradas |
|---|---|---|
| `Completed` com texto | o texto | `Completed` |
| `Completed` sem texto | nada | `Completed` |
| terminal não-`Completed` | o aviso | `Failed` |

Depois remove a linha e grava. O endpoint valida o token e chama o processador; a
reconciliação reivindica e chama o mesmo processador com a `AgentTask` do
`GetTask`, que é o mesmo payload do push.

O processador também expõe o encerramento por falha do lado do inbox (aviso,
`Failed`, remove), usado nos três caminhos de falha de `DebounceSweepService`
(`:142`, `:241`, `:268`) e pelo encerramento por idade (D7).

A entrega ao canal (`DeliverResponseAsync`, `:121-184`) passa do endpoint para o
processador **sem mudar**, com a consulta e a decifragem continuando dentro do
`try`. Três consumidores reais justificam a extração (convenção 2): o endpoint, a
reconciliação e a varredura de debounce.

### D6 — Aviso de falha: texto fixo, mesmo caminho da resposta

Uma constante no processador, igual para todo canal e agente, fixada pelo dono em
03/10/2026: *«Não consegui responder agora. Pode tentar de novo em instantes?»*. A
abrangência também é decisão do dono: todos os desfechos de falha, **inclusive** a
rejeição síncrona por agente inativo. Ele é entregue e persistido como
`Message` de saída, com o mesmo status de entrega da resposta (`Sent`, ou `Failed`
com motivo). A falha ao entregar o aviso não impede a remoção da linha.

**Quando NÃO avisa:** falha de transporte ainda reintentável (a linha volta a
`Pending`, como hoje) e task `Completed` sem texto.

### D7 — Linha sem `TaskId`: encerramento por idade, como perda

Linha em `Dispatching` com `TaskId` nulo e `LastMessageAt` mais velho que o limite
→ reivindicação pelo `xmin`, depois encerramento como o esgotamento de tentativas:
log `Error`, aviso, entradas `Failed`, linha removida. **Sem redisparo** (decisão do
dono): se o `SendMessage` tinha chegado à `apps/api`, o agente responderia duas
vezes.

**Depois de D11, D7 cobre crash do processo e exceção inesperada**, não a parada
normal. **O falso aviso que sobra:** se o `SendMessage` tinha chegado à `apps/api`
antes do crash, o agente respondeu, o push dele recebeu 401, a resposta se perdeu,
e o contato recebe *"Não consegui responder"* 10 min depois. *Alternativa, fora de
escopo:* procurar a task pelo `messageId` do `SendMessage` (o inbox o gera,
`DebounceSweepService.cs:185`) e reconciliar pelo estado dela. O `ListTasks` do
protocolo não filtra por mensagem (campos de `ListTasksRequest` no XML do SDK:
`ContextId`, `Status`, `StatusTimestampAfter`, `HistoryLength`, paginação). Há duas
formas: uma rota nova em `apps/api` que procure pelo `messageId`, ou, **sem rota
nova**, listar as tasks do `ContextId` da sessão com histórico e procurar o
`messageId` do lado do inbox, paginando um contexto que cresce com a conversa.

**Por que aqui há limiar e em D3 não:** sem `TaskId` não há a quem perguntar. O
limiar sai das contas, contado do `LastMessageAt`, já que não existe coluna com o
instante do claim:

- o claim acontece no primeiro ciclo depois de `LastMessageAt + Window` (10 s +
  até 2 s de `SweepInterval`);
- o `SendMessage` tem 5 s de timeout (`apps/inbox/Program.cs:100`), e o `TaskId` é
  gravado em seguida;
- os candidatos de um ciclo são processados **em série**, até 5 s cada. Um ciclo
  com fila acumulada afasta o claim do `LastMessageAt`;
- **10 min** cobre mais de 100 candidatos à frente no mesmo ciclo.

**Gatilho de recalibração:** `Window`, `SweepInterval` ou timeout do cliente A2A
mudando, ou disparo concorrente dentro de um ciclo.

*Descartada, coluna `DispatchingSince` (migration):* daria um limiar de ~1 min, mas
só encurta a espera numa fonte rara. Fica como alternativa se o resíduo de D7
aparecer (Risks).

**O instante que o D7 mede, verificado em 04/10/2026, e a decisão do dono no
mesmo dia: D7 mantido, sem `DispatchingSince`.** O `LastMessageAt` é o
`receivedAt` que o adapter passa a `IInboundMessageOrchestrator.ReceiveMessageAsync`
(`InboundMessageOrchestrator.cs:59` e `:115` → `PendingDispatch.cs:56` e `:63`;
uma falha de transporte o renova com o relógio do inbox, `:86`). Os dois
adapters de hoje passam o relógio do inbox, e não o do provedor:
`TelegramInboundWebhookHandler.cs:105` e `WahaInboundWebhookHandler.cs:64`, os dois
`DateTimeOffset.UtcNow`; os modelos de payload nem trazem o horário do provedor.
Então mensagem entregue com atraso pelo provedor nasce com `LastMessageAt` recente
e não dispara o D7. A regra está escrita no próprio contrato
(`IInboundMessageOrchestrator.cs`, comentário de `receivedAt`). O motivo de não
acrescentar a coluna agora, já que a migration existe por causa do D4: o risco
medido é praticamente nulo, e ela seria antecipação (convenção 2).

### D11 — A parada não interrompe o envio já reivindicado

O trecho entre o commit do claim e a gravação do `TaskId` vira uma unidade com
prazo próprio, e não obedece mais ao `stoppingToken`. O trecho tem a consulta do
canal, o `SendMessage` e o tratamento da resposta, inclusive as gravações de
falha de transporte e de rejeição. A parada espera a unidade:
`BackgroundService.StopAsync` aguarda o `ExecuteAsync` até o `ShutdownTimeout` do
host, que o inbox não altera. O candidato seguinte do ciclo observa o
`stoppingToken` na primeira consulta e encerra o laço, como hoje.

**Medido em 03/10/2026, sobre `cb74315`**, com o `SendMessage` falso levando 1 s e
a parada chegando no meio
(`Source1_InboxStopsWithSendMessageInFlight_StopWaitsAndTaskIdIsRecorded`):

| | parada | `TaskId` |
|---|---|---|
| código atual | **6 ms** | **nulo** (vermelho) |
| com a unidade de 8 s (patch provisório, revertido) | **1.009, 1.010, 1.008 ms** | **gravado** (verde, 3/3) |

E o pior caso, com a `apps/api` que nunca responde: a parada esperou o prazo
inteiro da unidade (o teste levou 13 s, ~8 s de parada + 3 s do segundo host +
arranjo), e a linha ficou sem `TaskId`, que é o caso de D7. Com o cliente real, o
timeout de 5 s do `HttpClient` (`apps/inbox/Program.cs:100`) dispara antes do prazo
da unidade, e a falha vira transporte reintentável: a linha volta a `Pending` e é
redisparada depois do boot, como hoje (leitura de `IsTransportFailure`, `:303-311`;
o fake não exercita o `HttpClient`).

**O prazo, derivado:** 5 s do cliente A2A + folga para as gravações = **8 s**,
abaixo dos **10 s** padrão do `stop_grace_period` do Compose
(`docker-compose.prod.yml`, serviço `inbox` na linha 113, sem
`stop_grace_period`). O `ShutdownTimeout` do host (30 s, padrão) não é o limite que
vale: o `SIGKILL` do Compose chega antes. **Gatilho de recalibração:** timeout do
cliente A2A, `stop_grace_period` do inbox, ou `ShutdownTimeout` explícito.

*Descartado, deixar a parada interromper e contar com D7:* é o falso aviso de D7 em
todo deploy com mensagem em voo, o caso mais frequente da fonte 1.

**A parada com vários trabalhos em voo (condição do portão 1), medida em
04/10/2026** com o `Program` real sob Kestrel e um cronômetro em cada
`IHostedService` (`ShutdownBudgetProbeTests`; contenção de outra worktree
rodando a suíte de `apps/workers`, load 10,7 → 5,1, tempos dominados pelos
atrasos simulados):

- **A ordem real** é inversa ao registro, em série (`ServicesStopConcurrently`
  padrão), mas o `GenericWebHostService` é registrado **no `builder.Build()`**,
  depois de todo `AddHostedService` (#255 contra #251 do debounce). Ele para
  **primeiro**. No `Program` desta change:
  **Kestrel → reconciliação → `DebounceSweepService` → DataProtection →
  HealthCheck**. O Kestrel espera as requisições em voo, e o push que está
  entregando é uma delas.
- **O trabalho em voo corre em paralelo**; só as esperas são em série. Com
  `SendMessage` de 3 s, push entregando 4 s e entrega na reconciliação em voo, a
  parada levou **3.968 ms**, e a unidade do debounce já tinha terminado quando
  chegou a vez dela.
- **O que estoura é reivindicar depois do pedido de parada.** Enquanto o Kestrel
  espera, o `stoppingToken` dos outros serviços ainda não foi cancelado. Com um
  push entregando 6 s e uma mensagem cuja janela venceu durante essa espera, o
  debounce **reivindicou 2,95 s depois do pedido de parada**, abriu uma unidade
  de 8 s, e a parada levou **10.933 ms**.

**Decisões (dono, 04/10/2026):**

- **Nenhuma reivindicação depois do pedido de parada.** O debounce e a
  reconciliação conferem `IHostApplicationLifetime.ApplicationStopping`, que é
  cancelado no instante do pedido, antes de cada reivindicação. O
  `stoppingToken` de cada serviço não serve para isso, porque só é cancelado na
  vez dele. Com isso, a parada dura o máximo do que **já** estava em voo.
  *Descartado, `ServicesStopConcurrently = true`:* teria o mesmo efeito, mas
  mudaria a parada do host inteiro.
- **Entrega em voo na reconciliação: espera com prazo** (escolha minha, dentro
  do item 1 da condição). O prazo vence **8 s depois do pedido de parada**,
  contado em paralelo com o da unidade do debounce, e não somado a ele.
  - *Resíduo:* entrega ao canal ainda em curso nesse instante é cancelada, e a
    linha fica reivindicada em `Dispatching`. O próximo boot a reconcilia de novo
    e pode entregar em dobro, se o provedor já tinha aceitado.
  - *Descartado, cancelar na hora:* tem o mesmo resíduo, mas em quase todo
    deploy com entrega em voo, já que a entrega típica dura centenas de ms.
- **Push em voo no endpoint além do prazo do Compose: resíduo aceito.** A
  entrega do endpoint usa `CancellationToken.None`, e os senders de canal têm
  100 s de timeout. O `SIGKILL` dos 10 s pode chegar entre a entrega e a
  gravação. Antes, a linha ficava órfã; com a reconciliação, ela é reconciliada
  e a resposta pode sair em dobro. Entrega típica não é afetada. *Descartados:*
  `stop_grace_period` explícito (toca o deploy) e prazo na entrega do endpoint
  (muda a semântica do push).

### D8 — Configuração

`DispatchReconciliationOptions` (seção `DispatchReconciliation`): `Interval` (1 min),
`TerminalGrace` (2 min, D3), `UntrackedDispatchMaxAge` (10 min, D7) e `ClaimLease`
(2 min, D4, acrescentado na implementação). São
configuráveis pelo mesmo motivo de `DebounceOptions`: os testes precisam de valores
curtos. Os valores de produção são os derivados acima, e o comentário de cada um
carrega a derivação e o gatilho de recalibração.

O `Interval` de 1 min equilibra a latência de recuperação (no pior caso, carência +
intervalo, ~3 min depois do estado terminal) com a carga em `apps/api`: um
`GetTask` por linha em `Dispatching` por ciclo.

### D9 — Linhas já existentes: limpeza manual, sem regra no código

Decisão do dono. Lido no dev em 03/10/2026, `buteco_inbox` × `buteco_agents`:

| quantas | task | quando | o que a primeira varredura faria |
|---|---|---|---|
| 2 | `Completed`, com resposta | 22/08, Telegram | entregaria respostas de 6 semanas atrás |
| 9 | `Failed` | 2 em 11/09, 7 em 22/09 | mandaria 9 avisos atrasados (WAHA) |
| 2 | `Submitted` | 22/09 | nada (não-terminal) |

As 13 têm `TaskId`. Antes do primeiro boot do inbox com esta change, **em cada
ambiente**, o dono lê e decide (tarefas 6.x). Para produção, é a mesma inspeção que
o `02` já previa para a janela do deploy.

*Descartado, corte de idade na entrega:* seria uma regra permanente para um
acúmulo que só existe no primeiro deploy.

### D10 — Guardas

Cada fonte tem guarda próprio, e cada guarda é um teste de reprodução da exploração
com a asserção trocada do defeito para a correção. A primeira perna (vermelho) é o
próprio teste da exploração, rodado de novo sobre o código atual antes da correção
(convenção 15).

- **Fontes 2 e 3**, mais "task `Failed` avisa": `tests/InboxOrchestratorRoundTrip.Tests`,
  com `GetTask` real contra `apps/api`. É o lugar onde o guarda reprova **no
  componente que a correção toca**: a reconciliação lendo o estado que o worker
  gravou.
- **Fontes 1 e 4**, a parada com envio em voo (D11), a parada sem reivindicação
  nova e a parada com entrega em voo na reconciliação (`Program` real sob Kestrel), a carência e o carimbo nulo, a
  reivindicação entre duas instâncias, o push depois da reivindicação, o endpoint
  que perde a corrida e o aviso em cada desfecho do `DebounceSweepService`:
  `apps/inbox/tests`. O `FakeA2AClientFactory` ganha um `GetTask` controlável.

Nenhum guarda depende de ordem que o banco às vezes produz sozinho. O critério é
estado persistido (linha presente/ausente, status, mensagem de saída), sempre lido
depois de um evento-âncora, nunca de relógio solto.

**Árvore (só o que muda):**

```
apps/inbox/src/Buteco.Inbox/
├── Program.cs                                        (registro do serviço e das opções)
└── Orchestration/
    ├── DebounceSweepService.cs                       (três falhas → processador; unidade com prazo, D11)
    ├── DispatchOutcomeProcessor.cs                   NOVO (desfecho + entrega + aviso)
    ├── DispatchReconciliationOptions.cs              NOVO
    ├── DispatchReconciliationService.cs              NOVO
    ├── Entities/PendingDispatch.cs                   (reivindicação: troca do token + ReconciliationClaimedAt)
    └── PushNotifications/Endpoints/PushNotificationEndpoints.cs  (valida token → processador)
apps/inbox/src/Buteco.Inbox/Infrastructure/Migrations/
└── *_AddReconciliationClaimedAt.cs                   NOVO (D4, divergência da implementação)
apps/inbox/tests/Buteco.Inbox.Tests/
├── OrphanDispatchReproTests.cs                       → guardas das fontes 1 e 4
├── DispatchReconciliationServiceTests.cs             NOVO
├── DispatchFailureNoticeTests.cs                     NOVO
├── PushNotificationEndpointsTests.cs                 (task Failed → aviso + Failed)
└── Support/FakeA2AClientFactory.cs                   (BeforeReturn; GetTask controlável)
tests/InboxOrchestratorRoundTrip.Tests/
├── OrphanDispatchReproTests.cs                       → guardas das fontes 2 e 3
└── Support/{RoundTripFixture.cs, PushNotificationGate.cs}
```

Nada em `libs/`.

## Risks / Trade-offs

- **[Entrega em dobro, resíduo 1]** push cujo processamento passa da carência
  (entrega ao canal lenta além de ~2 min) e reconciliação no mesmo ciclo → as duas
  entregam. → A carência é derivada do pior caso conhecido (D3), e o gatilho de
  recalibração está escrito. Guarda: push depois da reivindicação é rejeitado.
- **[Entrega em dobro, resíduo 2]** instância morre entre a reivindicação/entrega e
  a remoção → a linha continua em `Dispatching` e é reivindicada de novo quando o
  prazo de posse (2 min) vence. É entrega *ao menos uma vez*; o push de hoje tem o mesmo resíduo. →
  Aceito e escrito.
- **[Encerramento indevido, D7]** um ciclo do debounce com mais de ~100 candidatos
  à frente pode passar de 10 min entre `LastMessageAt` e o `TaskId`, e a linha seria
  encerrada com o `SendMessage` em andamento. → O limite tem folga de ordem de
  grandeza, e a alternativa (coluna `DispatchingSince`) está registrada.
  **Gatilho:** o primeiro log `Error` de encerramento por idade em produção é lido
  contra o log do `DebounceSweepService` do mesmo intervalo.
- **[D7 depende de o `LastMessageAt` ser o relógio do inbox]** achado na
  implementação e verificado em 04/10/2026; D7 mantido pelo dono. O limite de D7
  é contado do `LastMessageAt`, que vem do `receivedAt` passado a
  `IInboundMessageOrchestrator.ReceiveMessageAsync`.
  - **Hoje:** os dois adapters passam `DateTimeOffset.UtcNow`
    (`TelegramInboundWebhookHandler.cs:105`, `WahaInboundWebhookHandler.cs:64`), e
    o contrato agora diz que tem de ser assim (comentário de `receivedAt` em
    `IInboundMessageOrchestrator.cs`).
  - **Medido:** o teste de `messageInstant` do round-trip, que fixa `receivedAt` em
    10/03/2026, reprovou com o limite de 30 s, porque a linha foi encerrada antes
    de o `TaskId` ser gravado. No round-trip o limite foi elevado; o D7 tem
    guardas em `apps/inbox`.
  - **O risco que sobra com os adapters de hoje:** acúmulo na varredura do
    debounce. Uma linha só é reivindicada mais de 10 min depois do seu
    `LastMessageAt` se houver mais de 120 candidatos lentos (~5 s cada, com
    sucesso, porque quem falha por transporte renova o `LastMessageAt`) à frente no
    mesmo ciclo. E a reconciliação ainda teria de passar pela linha nos ≤5–8 s em
    que ela está sem `TaskId`: ~8–13% por linha afetada, condicionado a esse
    acúmulo. Com o `SendMessage` normal (~100 ms), 120 candidatos levam ~12 s, e o
    risco é praticamente nulo.
  - **Alternativa:** a coluna `DispatchingSince`, gravada no claim do debounce, para
    o D7 medir o tempo real em `Dispatching` sem depender do `receivedAt` nem do
    acúmulo. **Gatilho:** um adapter que precise passar outro instante que não o
    relógio do inbox, ou o primeiro log `Error` de encerramento por idade
    (`Mensagem de usuário perdida: disparo da sessão … sem TaskId`) em produção.
- **[Relógio]** a carência compara o `Status.Timestamp` gravado por `apps/workers`
  com o relógio de `apps/inbox`. → Desvio entre hosts é segundos contra 2 min.
- **[Aviso em todo desfecho de falha muda o que o contato vê]** canal ligado a um
  agente inativo passa a responder o aviso a cada mensagem (rejeição síncrona), e
  `apps/api` fora do ar por mais de `MaxDispatchAttempts` gera aviso. → Decisão do
  dono; o texto é neutro sobre a causa.
- **[Resposta muito atrasada]** se o inbox ficar fora do ar por horas, a
  reconciliação entrega respostas de horas atrás quando voltar. → Sem regra no
  código (D9); é o custo de não perder a resposta.
- **[Agente apagado]** sem a linha do agente, o cascade de `a2a_tasks` apaga a task,
  e a consulta responde `TaskNotFound`. Se o servidor A2A do agente não estiver em
  cache, responde `InvalidRequest` ("Agente não encontrado"); se estiver, cai no
  defeito da **#77**. Em qualquer caso, a linha nunca é reconciliada, e o log
  `Warning` **se repete indefinidamente**, uma vez por linha por intervalo (1 min).
  → Hoje `Agent` não tem rota de exclusão e o estado só existe por escrita direta no
  banco. **Gatilho, o mesmo da #77:** a primeira rota de exclusão de agente. Quem a
  criar decide o destino dessas linhas (encerrar como perda, por exemplo) e o
  limite do log.
- **[Falso aviso de D7]** crash do processo depois de o `SendMessage` chegar à
  `apps/api`: o agente respondeu, a resposta se perdeu (push 401), e o contato
  recebe o aviso de falha 10 min depois. → Raro depois de D11 (a parada normal não
  chega mais aqui). A alternativa por `messageId` está em D7, fora de escopo.
- **[Parada mais longa]** com envio ou entrega em voo, a parada do inbox leva até 8
  s, mais o tempo da entrega de push em voo. → O trabalho em voo é paralelo, e
  nenhuma reivindicação acontece depois do pedido de parada (D11, medido). O
  gatilho de recalibração está em D11.
- **[Entrega em dobro, resíduo 2 agravado pela parada]** push no endpoint cuja
  entrega ao canal passa do `stop_grace_period` (10 s) leva `SIGKILL` antes de
  gravar. A reconciliação depois entrega de novo. Entrega na reconciliação
  ainda em curso 8 s depois do pedido de parada é cancelada e refeita no
  próximo boot. → Aceito pelo dono (D11); só afeta entrega mais lenta que o
  prazo.
- **[Carga em `apps/api`]** um `GetTask` por linha em `Dispatching` por minuto, e a
  linha cuja task nunca termina é consultada para sempre. → Linha legítima vive
  segundos; a de task presa é a população que a detecção de `apps/workers` já
  reporta.
- **[Linha cuja task nunca termina continua órfã]** as 2 `Submitted` do dev são
  esse caso. → Fora de escopo; nomeado em Non-Goals.

## Migration Plan

1. **Antes** do primeiro boot do inbox com esta change, em cada ambiente: ler as
   linhas em `Dispatching` e o estado das tasks (consulta nas tarefas 6.x). O dono
   decide o que remover. Sem isso, a primeira varredura entrega o acúmulo.
2. **Deploy pelo runbook de redeploy** (`docs/deployment.md`, §2), porque há
   migration: `build` → `stop inbox` → `run --rm migrator` → `up -d`. A migration
   é uma só, `AddReconciliationClaimedAt`: acrescenta a coluna anulável
   `pending_dispatches."ReconciliationClaimedAt"`, sem valor padrão e sem
   preenchimento, então não reescreve linha nenhuma. Nenhuma variável nova é
   obrigatória (os padrões de D8 valem).
3. **Rollback, com a coluna presente:** voltar a imagem anterior de `apps/inbox`
   (`build` da versão anterior → `up -d inbox`), **sem** rodar o `Down` da
   migration. O código anterior não conhece a coluna, e o EF Core só lê e grava
   as colunas do próprio modelo, então a coluna anulável fica ignorada (leitura do
   EF, não medido com o código anterior). O efeito do rollback:
   - as linhas já reconciliadas continuam resolvidas, e as novas voltam a
     depender só do push;
   - **linha reivindicada pela reconciliação e ainda em `Dispatching`** no momento
     do rollback tem o token trocado, então o push dela recebe 401 e ela volta a
     ficar órfã, como antes da change. Ler `pending_dispatches` com
     `"ReconciliationClaimedAt" IS NOT NULL` na janela do rollback mostra quantas;
   - num redeploy posterior desta change, essas linhas voltam a ser
     reivindicáveis quando o prazo de posse (2 min) vence.

   Se o runbook completo for seguido com a imagem anterior, o `migrator` dela não
   desfaz a coluna: ele roda o `migrations bundle` do EF sem alvo
   (`deploy/migrate/entrypoint.sh`), que aplica as migrations que conhece e não
   reverte as que não conhece (leitura do EF, não medido). Remover a coluna
   (`Down`) só faz sentido se a change for abandonada de vez, e seria um passo
   manual, fora do runbook.

## Open Questions

Nenhuma. O texto e a abrangência do aviso foram fixados pelo dono (D6).
