## Context

`TaskJobConsumer` (`apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs`)
é o `BackgroundService` que consome a fila `agent-tasks`. O que o código faz hoje,
lido linha a linha em `f2e7b09`:

- `ExecuteAsync` (29-67) abre conexão e canal, registra um `AsyncEventingBasicConsumer`
  e fica em `Task.Delay(Timeout.Infinite, stoppingToken)`.
- O callback (45-62) **captura o `stoppingToken`** de `ExecuteAsync` e o passa a
  `AgentExecutionService.ExecuteAsync`, ao `BasicAckAsync` e ao `BasicNackAsync`.
  Não usa o `CancellationToken` que a biblioteca entrega no `BasicDeliverEventArgs`.
- `StopAsync` (69-84): `_channel.CloseAsync` → `_connection.CloseAsync` →
  `base.StopAsync`. É `base.StopAsync` que cancela o `stoppingToken`.

**Como o token chega à execução.** O mesmo `stoppingToken` atravessa
`AgentExecutionService.ExecuteAsync` inteiro (`AgentExecutionService.cs:93-505`) e
entra em `aiAgent.RunAsync` (462). A espera da delegação
(`AgentDelegationToolSetResolver.cs:312-372`) cria um token **ligado** a ele com
`CancelAfter(options.Value.Timeout)` — o prazo de 120 s — e o `catch` só trata como
timeout quando o token de fora **não** foi cancelado (366). Então a espera observa
o `stoppingToken`, mas o `stoppingToken` só é cancelado depois dos dois
fechamentos.

### O que o `RabbitMQ.Client` 7.2.1 faz no fechamento

Descompilado do assembly que o projeto compila (`Directory.Packages.props:51`,
`RabbitMQ.Client` 7.2.1, `lib/net8.0`), não de documentação:

- `Channel.CloseAsync(args, abort)` faz `ConsumerDispatcher.Quiesce()` (que cancela
  o `CancellationToken` das entregas), envia `channel.close`, espera o
  `close-ok` e então **`await ConsumerDispatcher.WaitForShutdownAsync(cancellationToken)`**
  — que é `_worker.WaitAsync(cancellationToken)`, e `_worker` é o laço
  `ProcessChannelAsync` que **executa o callback**. O `cancellationToken` é o que
  `StopAsync` recebeu do host; `WaitForShutdownAsync` engole o
  `OperationCanceledException` e retorna.
- `IConnectionExtensions.CloseAsync(connection, token)` usa
  `InternalConstants.DefaultConnectionCloseTimeout` = **30 s**, e
  `Connection.CloseAsync` faz `_mainLoopTask.WaitAsync(cts.Token)` com esse prazo,
  lançando `TaskCanceledException` quando ele estoura.

### Reprodução, por execução

Teste `StopAsync_WithExecutionInFlight_CancelsItAndReturnsPromptly`
(`TaskJobConsumerTests`): a chamada ao provedor espera pelo token que recebe —
mesma forma da espera da delegação —, o host é parado com ela em voo, e mede-se o
tempo de `host.StopAsync()`. Saídas completas em `~/buteco-runs/49/`.

| rodada | código | `ShutdownTimeout` | `StopAsync` | cancelamento chega à execução | `a2a_tasks` | `task_executions` | fila depois |
|---|---|---|---|---|---|---|---|
| 1 | `f2e7b09` | 30 s (padrão) | **60,1 s**, lança `TaskCanceledException` | **60,1 s** | `Working` | `ended_at` nulo | 1 mensagem |
| 2 | `f2e7b09` | 10 s | **40,0 s**, lança `TaskCanceledException` | **40,0 s** | `Working` | `ended_at` nulo | 1 mensagem |
| 3 | protótipo: só reordenar | 30 s | **0,1 s**, sem exceção | **0,0 s** | `Working` | `ended_at` nulo | 1 mensagem |

A rodada 2 é a que separa os dois prazos: **40,0 = 10 (host) + 30 (conexão)**, e a
rodada 1 é **30 + 30**. Nas rodadas 1 e 2 `base.StopAsync` **não chega a rodar**
(a exceção de `Connection.CloseAsync` sai antes); o cancelamento só chega quando o
`Dispose` do host cancela o `BackgroundService`, e é por isso que o fechamento da
métrica loga `ObjectDisposedException: IServiceProvider` — "a escrita final
encontrou o host descartado", como o `02` registrou em 21/09/2026.

**O 60 s do `02` é este mecanismo, e só este.** O "1m10s em `Connection.CloseAsync`"
da `delegacao-diagnostico` **não** é confirmado por isso: os dados daquela rodada
não existem, e esta change não afirma que o resolveu.

### O que acontece com a execução cancelada (pergunta 4), medido na rodada 3

Com o cancelamento chegando a tempo, o caminho é:

1. `RunAsync` lança `TaskCanceledException` (o `FunctionInvokingChatClient` a
   propaga — está na pilha da rodada 3).
2. O `catch` grande de `AgentExecutionService` (492-498) trata como falha comum:
   loga **erro** e chama `FailTaskAsync` **com o token já cancelado**, que lança
   `TaskCanceledException` em `TaskUpdater.FailAsync` antes de gravar.
   `MarkTerminal` não roda.
3. O `finally` fecha a métrica (com prazo próprio de 10 s, sem o token) e grava a
   linha com `ended_at`, `terminal_state` e `failure_phase` **nulos**.
4. A exceção chega ao `catch` do consumidor, que loga **erro** e chama
   `BasicNackAsync(requeue: false, stoppingToken)` — que **lança**, porque o token
   está cancelado. A mensagem fica sem `ack` nem `nack`, e o broker a devolve à
   fila quando o canal fecha.

**A guarda de estado terminal continua valendo:** ela só desvia task terminal, e a
task interrompida fica em `working`. Na reentrega ela cai no cenário "Mensagem
reentregue de task em `working` continua executando" (spec `a2a-task-lifecycle`).

**O resultado da rodada 3 é certo por acidente.** O `nack` foi escrito com
`requeue: false` — se ele não lançasse, a mensagem seria **descartada** e a task
ficaria em `working` para sempre. Ele lança só porque recebe o token cancelado.
Isso é o que a D2 decide.

### Varredura de escopo (convenção 22, oitava ocorrência)

Universo: todo `BackgroundService`/`IHostedService` em `apps/*/src`.

| critério | comando (resumo) | resultado | erro do critério |
|---|---|---|---|
| estreito | linha com `class` **e** o tipo-base | **0** | classes com construtor primário quebram a linha antes do `: BackgroundService` |
| largo | qualquer linha citando `BackgroundService\|IHostedService\|IHostedLifecycleService` | **5** linhas | `Program.cs:77` é comentário (falso positivo) |
| cruzamento | `AddHostedService` em `src` | **4** registros | — |
| conferido item a item | `-n` aberto em cada arquivo | **4 classes** | — |

| classe | arquivo:linha | sobrescreve `StopAsync`? | fecha recurso antes de cancelar? |
|---|---|---|---|
| `TaskJobConsumer` | `apps/workers/.../Messaging/TaskJobConsumer.cs:16`, `StopAsync` 69-84 | sim | **sim** — a #49 |
| `KnowledgeIndexingConsumer` | `apps/workers/.../Knowledge/Indexing/KnowledgeIndexingConsumer.cs:22`, `StopAsync` 113-126 | sim | **sim** — virou a **#125** |
| `NonTerminalTaskDetectorService` | `apps/workers/.../Diagnostics/NonTerminalTaskDetectorService.cs:128` | não (só `ExecuteAsync` 130) | não |
| `DebounceSweepService` | `apps/inbox/.../Orchestration/DebounceSweepService.cs:23` | não (só `ExecuteAsync` 25) | não |

Critério complementar, pelo **efeito** e não pelo tipo: toda chamada `.CloseAsync(`
em `apps/*/src` devolveu **5** linhas — as 4 dos dois consumidores e
`AgentExecutionService.cs:503` (`metricsWriter.CloseAsync`, homônimo, falso
positivo). `apps/api` não tem hosted service nem fechamento de conexão RabbitMQ.

A #125 **não entra** nesta change (convenção 23): o `catch` do
`KnowledgeIndexingConsumer` faz `nack` sem reentrega **de propósito** (para não
criar laço), e o que uma indexação interrompida deve virar é decisão própria.

### Produção

`docker-compose.prod.yml:133` (`workers`) não define `stop_grace_period`; vale o
padrão do Compose, **10 s** — leitura da documentação do Compose, não medido aqui.
O `ENTRYPOINT` de `apps/workers/Dockerfile:35` está em forma exec
(`["dotnet", "Buteco.Workers.dll"]`), então o `SIGTERM` chega ao processo .NET e
dispara a parada do host.
Com o defeito, o `SIGKILL` chega no meio do primeiro fechamento. Com a correção a
parada medida é 0,1 s, dentro de qualquer prazo.

## Goals / Non-Goals

**Goals:**

- A parada do worker com execução em voo cancela a execução **antes** de fechar
  canal e conexão, e retorna sem esperar prazo de fechamento nenhum.
- A execução interrompida termina com o host ainda vivo.
- O que a task e o job viram numa parada é **escolhido e escrito**, não
  consequência de um `nack` que lança.

**Non-Goals:**

- `KnowledgeIndexingConsumer` (#125).
- `PushNotificationSender` e `HttpClient` de saída (#46, #47).
- Drenagem graciosa (esperar a execução terminar antes de parar) — é a opção D
  abaixo, descartada nesta change; se for querida, pertence à `replicas-de-worker`.
- `stop_grace_period` do deploy.
- Abstração comum aos dois consumidores (convenção 2): o segundo consumidor é a
  #125, e ela decide se extrai.

## Decisions

### D1. Ordem da parada: cancelar o consumidor, cancelar a execução, fechar canal, fechar conexão

`StopAsync` faz, nesta ordem: `BasicCancelAsync` do consumidor (D5),
`base.StopAsync(cancellationToken)`, `_channel.CloseAsync`, `_connection.CloseAsync`.
A falha do `BasicCancelAsync` (broker já fora, por exemplo) é logada como aviso e
**não impede** o `base.StopAsync` — se impedisse, a execução voltaria a ser
cancelada só no `Dispose` do host, que é o defeito.

`base.StopAsync` cancela o `stoppingToken` e espera só `ExecuteAsync`, que sai do
`Task.Delay` na hora — o callback é outra tarefa, do dispatcher. Em seguida `_channel.CloseAsync` faz o que
já fazia: `Quiesce`, `close`, e espera o callback (`WaitForShutdownAsync`) — que
agora termina porque o cancelamento já chegou. Só depois a conexão fecha.

**Por que isso garante que a escrita final acontece antes do descarte:** a espera
pelo callback está dentro de `StopAsync`, e o host só descarta depois que
`StopAsync` retorna. Medido na rodada 3: sem `ObjectDisposedException`.

**Alternativa descartada — ligar o `deliverEventArgs.CancellationToken` ao
`stoppingToken` no callback.** A biblioteca cancela esse token no `Quiesce`, então
a execução seria cancelada pelo próprio `Channel.CloseAsync`, sem reordenar. Funciona
pela semântica interna do dispatcher de uma versão específica, enquanto a ordem do
D1 funciona pela semântica pública do `BackgroundService`. E deixaria a ordem errada
no lugar para o próximo leitor copiar.

### D2. O que a execução interrompida por parada vira — **B, decidida pelo dono em 03/10/2026**

Aprovada com condições: medir o laço de reentrega (D5), conferir o advisory lock
no caminho cancelado (D6), e os ajustes menores já incorporados. As quatro opções
ficam registradas com o custo de cada uma, como foram apresentadas.

Quatro opções. Todas partem do D1; diferem no que a task e o job viram.

**A — só o D1.** Nenhuma outra mudança.

- Resultado: o medido na rodada 3 — `working`, linha de métrica aberta, job de
  volta à fila **porque o `nack` lança**.
- Custo: o resultado certo depende de `BasicNackAsync` lançar com token cancelado,
  que é comportamento da biblioteca, não contrato nosso. Cada parada com execução
  em voo loga **dois erros** ("Falha ao executar o agente", "Falha ao processar
  mensagem"), indistinguíveis de falha real. Menor diff.

**B — D1 + parada como caminho próprio, com a mensagem devolvida explicitamente.**
*(recomendada)*

- `AgentExecutionService`: os dois `catch` que chamam `FailTaskAsync` (aquisição do
  lock, 286; execução, 492) deixam passar o `OperationCanceledException` quando
  **o token da execução** foi cancelado, sem tentar gravar `failed`. O `finally` da
  métrica continua fechando a linha (nula: a execução não terminou, que é o que ela é).
- `TaskJobConsumer`: um `catch (OperationCanceledException) when
  (stoppingToken.IsCancellationRequested)` antes do `catch` geral faz
  `BasicNackAsync(requeue: true, CancellationToken.None)` e loga **informação**
  ("execução interrompida pela parada, job devolvido à fila"), não erro.
- Resultado: o mesmo estado persistido da A, agora por decisão e com guarda.
  Reentrega cai em "task em `working` continua executando", que já existe e já tem
  teste.
- Custo: ~2 cláusulas `when` e um `catch` novo; um teste a mais. A reentrega
  **reexecuta do zero** (o LLM é chamado de novo). Se a execução interrompida era
  uma origem de delegação, a task alvo já publicada **continua sozinha** e a
  reexecução cria **outra** — trabalho em dobro no alvo, cujo primeiro resultado
  ninguém lê. Isso já acontece hoje em toda morte de worker; a B não piora, só não
  resolve.

**C — D1 + terminar em `failed` na parada.** Gravar `failed` com um token próprio
de prazo curto (não o cancelado), disparar a push notification, e confirmar o job.

- Resultado: task `failed`, linha fechada, sem reentrega, sem reexecução e sem
  delegação em dobro.
- Custo: **todo deploy com mensagem em voo vira uma resposta de erro para o
  usuário**, que hoje não recebe nada e na reentrega recebe a resposta certa.
  Contraria o requisito "task em `working` continua executando" no espírito: troca
  recuperação por falha. A `failure_phase` precisaria de um valor novo (`shutdown`),
  e os valores de fase são espelhados em `apps/api`
  (`ExecutionMetricsSchemaMirrorTests`) — fora do escopo declarado. E dispara a
  push notification, que é território da #46/#47.

**D — drenagem graciosa.** Na parada, `BasicCancel` do consumidor e esperar a
execução em voo terminar até um prazo, e só então cancelar (caindo em B).

- Resultado: execução curta termina normalmente; longa cai em B.
- Custo: o prazo útil é o `stop_grace_period` de produção (10 s, padrão) menos
  folga, enquanto a espera de delegação vai a 120 s — na prática, o caso que
  motivou a issue cairia em B do mesmo jeito. Mais código, configuração nova, e
  mexe no ciclo de vida das instâncias, que é exatamente o assunto da
  `replicas-de-worker`.

**Recomendação: B.** É o menor passo que torna o resultado da parada uma
**decisão escrita e guardada** em vez de efeito colateral de um `nack` que lança;
preserva o comportamento que o sistema já escolheu para worker que morre no meio
(reentrega em `working`); não mexe em contrato com `apps/api` nem em push
notification. A cobra a mesma correção de tempo e deixa o acidente no lugar. C
troca recuperação por erro visível ao usuário em todo deploy. D só compensa com
`stop_grace_period` maior que a espera de delegação, e isso é decisão de deploy e
da `replicas-de-worker`.

*O `tasks.md` e o delta de spec estão escritos para B.*

**O que a B não tinha previsto, e a revisão do dono apontou:** o `nack(requeue:
true)` acontece com o consumidor ainda registrado no broker. É o D5.

### D3. Onde o guarda mora

Em `TaskJobConsumerTests`, com o `BuildHost` que já existe — **sem classe de host
nova**. Uma 15ª classe na `WorkerHostCollection` dispararia a recalibração dela
(convenção 22; o critério escrito lá é "recalibrar quando entrar a 15ª classe").
O teste reprovou sobre o código atual (rodadas 1 e 2) e passou no protótipo do D1
(rodada 3) — as duas pernas da convenção 15 já medidas para o tempo; a parte da B
(mensagem devolvida por `nack` explícito, sem log de erro) ganha asserções próprias
no apply e precisa reprovar sobre A antes de passar sobre B.

### D4. O caminho da tool também é guardado

A rodada 3 prova que o cancelamento atravessa `RunAsync` quando é a **chamada ao
provedor** que espera. A espera da delegação é uma **tool**, invocada pelo
`FunctionInvokingChatClient`, que tem `catch` próprio para exceção de tool. Se ele
transformasse o `OperationCanceledException` da tool em resultado de erro, a
execução seguiria em vez de parar.

Descompilado de `Microsoft.Extensions.AI` 10.6.0 (`lib/net10.0`, a versão em
`project.assets.json`), `FunctionInvocationProcessor`: a exceção da tool só vira
resultado sob `catch (Exception) when (captureExceptions &&
!cancellationToken.IsCancellationRequested)`, e o `cancellationToken` ali é o de
`RunAsync` — o `stoppingToken`, do qual o token da espera da delegação é ligado.
Com a parada, o filtro é falso e o cancelamento **sobe**. A leitura vale para esta
versão; **o guarda é por execução no apply** (tarefa 2.4): uma tool que espera pelo
token, em voo na parada.

### D5. A B reentrega em laço sem cancelar o consumidor no broker — `BasicCancelAsync` primeiro

**O mecanismo.** Com `prefetchCount: 1`, o `nack(requeue: true)` libera o slot do
consumidor, e o broker pode entregar a mesma mensagem **ao mesmo consumidor**, que
continua registrado até o `Quiesce` do `Channel.CloseAsync`. A entrega nova chega
com o `stoppingToken` já cancelado, a primeira leitura de banco lança
`OperationCanceledException`, o `catch` da parada faz outro `nack(requeue: true)`,
e assim por diante. A rodada 3 não exercitava isso porque o `nack` lançava.

**Medido com o protótipo da B** (`~/buteco-runs/49/prototipo-B-*.txt`):

| rodada | ordem no `StopAsync` | entregas do mesmo job durante a parada | chamadas ao client | logs `>= Error` |
|---|---|---|---|---|
| 1-10 | `base.StopAsync` → fecha | **1** em cada uma das 10 | 1 | 0 |
| 11 | `base.StopAsync` → **espera 2 s** → fecha | **649** (`DeliveryTag` 1→649, `Redelivered=True` a partir da 2ª) | 1 | 0 |
| 12 | **`BasicCancelAsync`** → `base.StopAsync` → espera 2 s → fecha | **1** | 1 | 0 |
| 13 | guarda na entrada do callback → `base.StopAsync` → espera 2 s → fecha | **2** (a 2ª recusada e deixada sem `ack`) | 1 | 0 |

**As dez rodadas limpas não absolvem a ordem sem cancelamento:** o laço depende de
o `nack` chegar ao broker antes do `Quiesce`, e isso é corrida. Nesta máquina, sem
espera artificial, o `Quiesce` venceu 10 de 10; com 2 s entre os dois, o laço deu
649 voltas. Uma parada em que o fechamento do canal demore — a própria
`BasicCancel`/`Close` são RPCs com `ContinuationTimeout` de 20 s — abre a janela
em produção.

**Escolha: `BasicCancelAsync` como primeiro passo de `StopAsync`.**

- É o "pare de me entregar" do protocolo: depois do `cancel-ok`, o broker não
  entrega nada a este consumidor, e o `nack(requeue: true)` devolve o job à fila
  para **outro** worker (ou para depois). O `deliveryTag` em voo continua válido no
  canal, então o `nack` posterior funciona — medido na rodada 12.
- **Contra o guarda na entrada do callback** (rodada 13): o guarda também segura o
  laço, mas recebe uma entrega a mais e a deixa sem `ack`, ocupando o slot até o
  canal fechar. Ele funciona **porque** o `prefetchCount` é 1; com prefetch maior,
  a quantidade de entregas presas cresce junto, e o mecanismo vira dependência de
  uma configuração que nada liga a ele. E é uma terceira regra de liquidação
  (`ack`, `nack`, "nem um nem outro") para quem lê o callback.
- **Custo do `BasicCancelAsync`:** um RPC a mais na parada.
- **Prazo próprio de 2 s, não o token de `StopAsync`** (acréscimo do dono no ok do
  apply). O token de `StopAsync` é o `ShutdownTimeout` do host, 30 s por padrão —
  maior que os 10 s do `stop_grace_period` padrão do Compose. Uma demora do
  `cancel-ok` presa a ele (broker lento ou inalcançável na hora da parada) seguraria
  o `base.StopAsync`, e a execução voltaria a ser cancelada tarde: o defeito da #49
  por outro caminho. Com o broker saudável o `cancel-ok` é uma ida e volta de
  milissegundos; 2 s deixam ~8 s do prazo do Compose para o resto da parada. Ao
  estourar ou falhar: aviso no log e segue para `base.StopAsync`, e o laço que o
  cancelamento evitaria fica limitado à janela até o `Quiesce` (rodada 11).
  **Sem teste da demora:** provocá-la exigiria pausar o contêiner do broker, e
  aí o fechamento do canal e da conexão também demoram (30 s da conexão), o que
  mede outra coisa. Só o registro, como o dono previu.

**O guarda do D5 é determinístico, não estatístico:** no instante em que a
execução observa o cancelamento, a fila já tem **zero** consumidores deste worker
(`QueueDeclarePassive` → `ConsumerCount`). Sem o `BasicCancelAsync` primeiro, o
consumidor ainda está registrado nesse instante — reprova sempre, sem depender de
corrida. Tarefa 2.3.

### D6. O advisory lock é liberado no caminho cancelado

`ConversationContextLock.DisposeAsync` (`ConversationContextLock.cs:126-142`) faz o
`pg_advisory_unlock` com **`CancellationToken.None`** de propósito (comentário nas
linhas 132-134), e o `await using` está fora do `try` da execução
(`AgentExecutionService.cs:304`) — então roda no caminho cancelado da B, que deixa
o `OperationCanceledException` atravessar o `catch` e sair pelo `using`.

Medido nas rodadas 1-13: `pg_locks` com o advisory lock do par
`(hashtext(agentId), hashtext(contextId))` **zero** depois da parada, nas 13. A
execução estava com o lock em posse no momento da parada (a chamada ao client é
posterior ao `AcquireAsync`). A afirmação de que a contagem era **1** antes da
parada não foi medida nessas rodadas — sem ela o zero não prova a liberação, só a
ausência; a tarefa 2.5 mede as duas pontas. O host de teste e o host da reentrega
dividem o pool do Npgsql (mesma connection string, mesmo processo), que é o
cenário em que um lock não liberado apareceria: o Npgsql **não** libera advisory
lock ao devolver a conexão ao pool (comentário em `ConversationContextLock.cs:47-55`).

Como o unlock roda, **não há comentário na #46** a fazer.

**Observação no código ANTIGO, inconclusiva, registrada para não ser lida como
mais do que é.** Contra `f2e7b09`, os guardas leram `pg_locks` = **1** logo depois
da parada em 3 das 5 execuções. Lá o cancelamento só chega no `Dispose` do host e a
limpeza da execução continua de forma assíncrona depois dele; nenhum erro do
`DisposeAsync` do lock aparece no log (o `ObjectDisposedException` registrado é do
fechamento da métrica). A leitura não separa "o unlock não rodou" de "o unlock
ainda não tinha rodado", então **não** é evidência para a pista 1 da #46. Em
produção, o fim do processo fecha a conexão e o Postgres libera o lock de qualquer
forma.

### Divergências da implementação

Nenhuma de forma: a implementação segue D1-D6. Duas coisas que o design não
previa e o apply mediu: o ruído de log da reentrega (#126, acima) e a observação
inconclusiva do lock no código antigo (D6). O `BuildHost` de `TaskJobConsumerTests`
ganhou dois parâmetros opcionais (logs capturados e o resolver de delegação) para os
guardas, sem mudar os testes que já o usavam.

## Árvore de pastas proposta

```
apps/workers/
├── src/Buteco.Workers/
│   ├── Agents/AgentExecutionService.cs        (catch: deixa passar o cancelamento da parada)
│   └── Messaging/TaskJobConsumer.cs           (StopAsync: BasicCancel, cancelar, fechar; catch da parada com nack requeue)
└── tests/Buteco.Workers.Tests/
    └── TaskJobConsumerTests.cs                (guardas da parada, do laço, da tool em voo, da reentrega)
openspec/changes/workers-parada-com-execucao-em-voo/
├── proposal.md
├── design.md
├── tasks.md
└── specs/a2a-task-lifecycle/spec.md
```

Nada em `libs/`.

## Risks / Trade-offs

- **Reentrega abre a linha de métrica de novo** → `task_executions` tem chave em
  `TaskId` (`AppDbContext.cs:248`); na reentrega o `OpenAsync` falha por chave
  duplicada, e o `CloseAsync` atualiza a linha existente. Isso já acontece hoje em
  toda morte de worker; com B passa a acontecer em todo deploy com mensagem em voo.
  **Medido no apply (tarefa 2.5):** o dado sai certo (linha fechada em
  `Completed`), mas cada reentrega loga **duas linhas `fail:` do EF Core**
  (`Database.Command[20102]`, `Update[10000]`) e um aviso de métrica. É ruído novo
  em todo deploy, e virou a **#126** na hora (convenção 23); a correção não entra
  aqui.
- **Cancelamento depois do estado terminal gravado** → se a parada chega entre
  gravar `completed` e disparar a push notification (`AgentExecutionService.cs:490`),
  a push recebe token cancelado; com B, a mensagem volta à fila, a reentrega acha a
  task terminal e só confirma — **a push não é reenviada**. Janela estreita, existe
  hoje igual, e o envio é território da #46/#47. **Contraparte:** registrado aqui e
  citado na entrega; não testado nesta change.
- **Delegação em dobro na reexecução** → descrito na B. Não piora com a change.
- **Timeout HTTP coincidente com a parada é tratado como parada** → o filtro da B
  é "`OperationCanceledException` **e** token da execução cancelado". O
  `HttpClient` sinaliza o seu timeout com `TaskCanceledException`; se ele estourar
  no mesmo instante em que a parada cancela o token, a execução é classificada como
  interrompida pela parada e o job é reentregue, em vez de terminar em `failed`. A
  reentrega reexecuta, e um provedor que continua lento leva ao `failed` normal na
  segunda tentativa. Aceito: a janela é a coincidência dos dois eventos, e o erro
  vai para o lado de tentar de novo, não de perder a resposta.
- **`BasicCancelAsync` que falha ou demora** → D1/D5: prazo próprio de 2 s; falha
  ou estouro é aviso e a parada segue.
- **Broker inalcançável na hora da parada** → o fechamento do canal e da conexão
  continua sujeito aos prazos da biblioteca (30 s da conexão). Fora desta change:
  é outra causa de parada lenta, não a #49, e não foi medida.
- **A ordem nova depende de `ExecuteAsync` sair rápido no cancelamento** → hoje ele
  sai do `Task.Delay` na hora; se alguém puser trabalho síncrono depois dele, a
  parada volta a esperar. O guarda de tempo pega.

## Migration Plan

Sem migração de dados nem de configuração. Deploy normal; a primeira parada já usa
a ordem nova. Rollback é reverter o commit.

## Open Questions

Nenhuma. O D2 foi decidido pelo dono em 03/10/2026 (B, com as condições do D5 e
do D6).
