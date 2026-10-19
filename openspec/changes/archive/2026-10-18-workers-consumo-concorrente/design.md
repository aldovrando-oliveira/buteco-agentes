## Context

`TaskJobConsumer` (`apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs`)
abre uma conexão própria, um canal, declara `agent-tasks` e chama
`BasicQosAsync(0, prefetchCount: 1, global: false)` (linha 52). O canal é criado com
`CreateChannelAsync(cancellationToken: …)`, sem `CreateChannelOptions`. O callback
do `AsyncEventingBasicConsumer` executa a task, confirma com `ack`, devolve com
`nack(requeue: true)` quando a parada cancela, e descarta com
`nack(requeue: false)` nas outras falhas. A parada (`StopAsync`) tem ordem fixada
pela change `workers-parada-com-execucao-em-voo`: `BasicCancel` com prazo próprio de
2 s, depois `base.StopAsync` (que cancela o `stoppingToken`), e só então o
fechamento do canal e da conexão.

Hoje uma instância executa uma task por vez. A delegação espera a task do Target
por polling (`AgentDelegationToolSetResolver.WaitForTerminalStateAsync`), segurando
o único slot da instância. Daí o autodeadlock numa instância só, que
`AgentDelegationConcurrencyTests` prova, e a contradição registrada no `02`: o
compose proíbe `replicas > 1`, e a delegação precisa de dois lugares.

### Achados que fixam o desenho (RabbitMQ.Client 7.2.1, decompilado)

1. **Só aumentar o prefetch NÃO paraleliza.** A concorrência de execução vem do
   `ConsumerDispatchConcurrency`, que se define em `CreateChannelOptions` (por canal)
   ou em `ConnectionFactory`. O canal tem precedência.
2. **A pegadinha:** o construtor público de `CreateChannelOptions` recebe
   `consumerDispatchConcurrency = 1` como default, e não `null`. Omitido o
   argumento, ele fixa 1 **por cima** do que a factory disser.
3. **O dispatcher é um `Channel<WorkStruct>` ilimitado** alimentando C tarefas.
   Com prefetch > concorrência, as entregas excedentes ficam presas no processo,
   fora do alcance de outras instâncias. É ali que a task do Target fica parada
   enquanto o Source espera por ela: **autodeadlock mesmo com várias réplicas**.
4. `ack` e `nack` concorrentes no mesmo canal são seguros.
5. `CloseAsync` faz `Quiesce` e `Task.WhenAll` das C tarefas do dispatcher. O
   cancelamento é paralelo (o `stoppingToken` é compartilhado), então o tempo de
   parada é o da execução mais lenta, não a soma.
6. A 7.2.2 não mexe no dispatcher. Atualizar fica fora.

### Recursos compartilhados entre execuções do mesmo processo

Verificados e sem impedimento: `ExecutionMetricsScope` é `AsyncLocal` e fica
isolado por fluxo (`ExecutionMetricsScopeTests.TwoConcurrentScopes_DoNotMixTheirCalls`
já afirma isso no nível unitário); `ChatClientResolver` já usa lock no cache;
MCP é HTTP sem estado de processo; o Npgsql fica bem abaixo do pool com `N`
pequeno (ver D3). O `ConversationContextLock` abre **uma conexão dedicada por
aquisição**, então duas execuções do mesmo processo são duas sessões Postgres
distintas: o lock as serializa entre si exatamente como serializa duas
instâncias (o `pg_advisory_lock` é reentrante só dentro da mesma sessão, que aqui
nunca é compartilhada).

**Delegação:** o Target usa outro `agentId`, então pega outro lock. Com prefetch =
concorrência, a conta de capacidade passa a ser `réplicas × N ≥ C × D + 1`, em
slots e não em instâncias.

## Goals / Non-Goals

**Goals:**

- Uma instância de `apps/workers` executa até `N` tasks ao mesmo tempo, com
  prefetch e concorrência sempre iguais.
- Delegação concluída numa instância só, com `N ≥ 2`.
- `N` checado no boot, com teto justificado.
- `N = 1` como rollback exato, provado pelos testes que o fixam.
- Parada com `N` execuções em voo dentro dos 10 s do compose, sem mudar a
  sequência da parada.

**Non-Goals:**

- `KnowledgeIndexingConsumer`: continua com 1 (D6).
- Calibrar `N` com o `C` de pico medido, decidir réplicas por disponibilidade e
  remover o `NonTerminalTaskDetector`: ficam na #48.
- Corrigir #145 (`CHANNEL_ERROR` derrubando o canal) e #126 (reentregas reabrindo
  `task_executions`): são registrados como riscos, não corrigidos aqui.
- Atualizar `RabbitMQ.Client` para 7.2.2.
- Mudar `replicas` no compose.

## Decisions

### D1. Uma opção só, `TaskConsumer:MaxConcurrentExecutions`, para prefetch e concorrência

Uma classe nova, `TaskConsumerOptions` (`SectionName = "TaskConsumer"`), com
`MaxConcurrentExecutions`. O valor entra em **dois** lugares do canal de tasks, e
só nele:

- `BasicQosAsync(0, prefetchCount: (ushort)N, global: false)`;
- `CreateChannelAsync(new CreateChannelOptions(…, consumerDispatchConcurrency: (ushort)N))`.

**Nunca na `ConnectionFactory`**, pelo achado 2: o construtor de
`CreateChannelOptions` impõe 1 por cima dela. E nunca em `RabbitMqOptions`, que
também serve ao publisher (`RabbitMqTaskJobPublisher`) e ao consumidor de
indexação; uma opção ali seria lida por quem não deve.

**Recusada: duas opções independentes** (prefetch e concorrência). Elas podem
divergir, e prefetch > concorrência reabre o autodeadlock (achado 3) sem nenhum
erro visível. Concorrência > prefetch é inócuo, mas também é uma configuração que
ninguém pediu.

**O guarda 1 (D7) é o que prende a pegadinha do construtor.** Ele reprova se a
concorrência voltar a 1, que é o que acontece se alguém "simplificar" a criação do
canal omitindo `consumerDispatchConcurrency`, ou mover o valor para a factory
achando que é equivalente. Isso vai escrito no comentário do código, ao lado da
criação do canal, e no XML doc do guarda: **quem apagar ou afrouxar esse guarda
reabre o defeito sem nenhum outro teste perceber.**

### D2. Default 4, declarado não medido

Com `N = 4`, uma instância atende uma cadeia de profundidade até 3 (Source + 3
Targets em espera, `D + 1 = 4` slots), ou duas delegações de profundidade 1 ao
mesmo tempo (`2 × 2 = 4`). É escolha de forma, não de medição: **o `C` de pico não
foi medido.** O XML doc da opção, o `docs/configuration.md` e o `02` dizem isso, com
o gatilho de recalibração (convenção 22): o `C` de pico observado pela coluna de
`Submitted` do `NonTerminalTaskDetector`. **Posição:** a #48.

**Este default é o que obriga o merge depois da #162** (ver proposal): com 4 slots,
a segunda mensagem do mesmo contato cai em execução simultânea com frequência muito
maior. Mergear com default 1 e subir depois só vale com esta decisão revista e
escrita.

### D3. Validação no boot, faixa `[1, 32]`

Extensão `ValidateTaskConsumerConcurrency(this IHost host)`, no idioma de
`ValidateEmbeddingBatchSize`: chamada do `Program.cs` sobre o host construído,
**reprova** o boot fora da faixa com mensagem que nomeia o valor encontrado, a faixa
e o motivo do teto; **não corrige** automaticamente; quando passa, **uma linha de
log** com o valor em vigor (requisito "Checagem de inicialização bem-sucedida
registra uma linha" do `workers-scaffold`).

**Por que 0 reprova:** `prefetchCount: 0` no RabbitMQ significa **sem limite**, e
`consumerDispatchConcurrency: 0` é inválido. O valor mais fácil de digitar errado é
o que mais quebra.

**Por que o teto é 32.** Cada execução segura **uma conexão dedicada** ao lock de
contexto durante toda a seção crítica (`ConversationContextLock`), e abre, enquanto
isso, conexões de vida curta para o store (escopos por operação, leitura de
delegação, busca de conhecimento). A conta de pior caso é **duas conexões por
execução ao mesmo tempo**: 32 × 2 = 64, mais o consumidor de indexação, o
`NonTerminalTaskDetector` e a checagem de consistência do boot, contra o
`Maximum Pool Size` default de 100 do Npgsql. Sobra margem. Um `N` perto de 100
esgota o pool, e o processo deixa de conseguir falar com o banco, **inclusive para
gravar a própria falha**: é o mesmo modo de quebra que
`RepeatedAcquisitionFailures_DoNotExhaustTheConnectionPool` já guarda para o lock.

**O "duas por execução" é premissa a conferir, não medição.** A tarefa 1.3 confere
por leitura de `AgentExecutionService` que nenhuma execução segura mais de duas
conexões ao mesmo tempo. Se segurar `k`, o teto passa a `⌊(100 − 8) / k⌋` e esta
decisão é reescrita antes do código.

**O teto de 32 protege o pool do PROCESSO, não o servidor.** O limite que trava
primeiro é outro: o `max_connections` do servidor Postgres (padrão 100), que
`apps/workers` divide com `apps/api` e `apps/inbox` (dois bancos num servidor só).
Um `N` dentro da faixa do boot pode passar na checagem e ainda assim esgotar o
servidor, e aí quem falha não é só o worker: `apps/api` e `apps/inbox` deixam de
abrir conexão também. A checagem de boot não vê isso, porque não conhece o consumo
dos outros apps.

**Gatilho: subir `N` acima do valor verificado na tarefa 6.1 exige refazer a 6.1
antes.** A 6.1 verifica o `max_connections` para um `N` específico (o default 4 no
primeiro deploy) e registra esse valor no `02`. Esse é o `N` verificado. A faixa
`[1, 32]` diz o que o processo aceita; o `N` verificado diz o que o servidor
comporta. Baixar `N` não exige nada (é o rollback, D4). O `docs/configuration.md`
escreve o mesmo gatilho ao lado da opção (tarefa 5.4).

### D4. Rollback declarado: `N = 1`

Com `N = 1`, o canal fica com prefetch 1 e concorrência 1, que é **exatamente** o
estado de hoje: `CreateChannelOptions` com concorrência 1 é o que o construtor já
fixava implicitamente, e o `BasicQos` é o mesmo. O rollback é configuração
(`TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS=1` no `.env.prod`), sem redeploy de
imagem.

**A prova de que `N = 1` reproduz o comportamento atual são os testes que o fixam**
(D7): eles foram escritos contra `prefetchCount: 1` e continuam passando, sem
mudança de asserção, com `N = 1` no harness.

### D5. A parada não muda de sequência

`BasicCancel` → `base.StopAsync` → `nack(requeue: true)` por execução → fechamento.
Com `N` execuções em voo, o `base.StopAsync` cancela o `stoppingToken` uma vez, e as
`N` observam o cancelamento **ao mesmo tempo** (achado 5). Cada uma libera o
próprio lock no `DisposeAsync` do `ConversationContextLock`, e os `N` jobs voltam à
fila. **Corrigido no apply, medido por mutação: quem os devolve é o fechamento do
canal, e não o `nack(requeue: true)` de cada execução.** `CloseAsync` envia
`channel.close` antes de esperar os callbacks (7.2.1 decompilado), o broker devolve
toda entrega sem `ack`, e o `nack` costuma sair com o canal já fechado; removido ou
trocado por `requeue: false`, o guarda 3 e o guarda 2.2 da change da parada ficam
verdes. O resultado que esta decisão promete (jobs de volta à fila, tasks em
`working`, locks soltos) vale; o caminho que ela descrevia, não. Corrigir mexe na
sequência da parada, que esta change não muda: **#177**. O `CloseAsync` espera as `N`
tarefas do dispatcher terminarem. O tempo total é o da execução mais lenta a
observar o cancelamento, não a soma, e cabe nos 10 s do `stop_grace_period` padrão
do compose (que o `docker-compose.prod.yml` não sobrescreve).

O comentário de `StopAsync` sobre a reentrega ("com o consumidor ainda registrado e
`prefetchCount: 1`, o broker o entrega de novo a ESTE consumidor") passa a valer para
`N` jobs, e é reescrito para não afirmar prefetch 1.

### D6. `KnowledgeIndexingConsumer` fica fora

Conexão e factory próprias, continua com prefetch 1 e concorrência 1 (o default do
construtor, que ele já usa sem saber). O teto de concorrência da indexação é
controlado na entrada da fila (`knowledge_indexing_requests`), não no consumidor. O
comentário dele e o de `KnowledgeIndexingQueues` (`apps/api`) dizem que o consumidor
de tasks "roda com `prefetchCount: 1`" e que isso "serializa o consumo dentro de uma
instância": **o motivo da fila própria continua válido** (indexação de minutos
ocuparia slots de execução de agente), só a frase muda. Em `apps/api` a mudança é
só de comentário, sem código.

### D7. Testes (convenção 15)

**Harness:** `TaskConsumerOptions` sem configuração vale 4 (D2), e os 14 harnesses
que registram `TaskJobConsumer` à mão (16 pontos de registro, contados sobre
`1e250e6`) herdam esse default. Os que dependem de `N = 1` passam a fixá-lo
**explicitamente**, com o motivo escrito ao lado:

| harness | por que depende de `N = 1` |
|---|---|
| `TaskJobConsumerTests` (sentinela, linha 605) | a sentinela publicada depois só termina depois da terminal **porque** o consumo é serializado |
| `OrphanDispatchRoundTripTests` via `RoundTripFixture` (linha 108) | mesma âncora de sentinela, no round-trip |
| `SessionSerializationRoundTripTests` via `RoundTripFixture` (linhas 37–40 e 69–75) | a perna vermelha precisa da primeira instância **ocupada**: msg2 só cai na segunda instância, a do `Command Timeout` curto, porque a primeira tem um slot só (ver abaixo) |
| `ConversationContextLockFailureTests` (linhas 186–191) | a primeira instância precisa estar **ocupada** para a segunda task cair na segunda instância e disputar o lock |
| `AgentDelegationConcurrencyTests` (`BuildHost`) | os cenários de instância única (`SingleInstance_DelegationTimesOut…`, `DelegationTimeout_WithTargetNeverConsumed…`, `DelegationTimeout_WithNoSuccessfulRead…`) **são** o caso de um slot; com 4 o Target seria consumido pela mesma instância, sem client mapeado, e o estado observado deixaria de ser `Submitted` |
| `AgentDelegationExecutionTests` (`BuildHost`) | a docstring afirma que uma instância nunca completa uma delegação; fica verdadeira com `N = 1`, e os cenários de duas instâncias continuam exercitando o consumo entre réplicas |

`SingleInstance_DelegationTimesOut…` **continua válido** com `N = 1`: é o cenário
"Instância única expira pelo timeout" da spec modificada. A tarefa 1.4 confere os
outros nove harnesses (lista em `tasks.md`) e registra, por harness, se depende ou
não de `N = 1`.

**`RoundTripFixture`: `N = 1` fixo, convivendo com os controles da #162.** A #162
acrescentou três controles neutros por padrão: atraso do LLM (`ChatClientDelay`),
`Command Timeout` do worker (`WorkerCommandTimeoutSeconds`, que só vale para hosts
construídos depois de atribuído) e segunda instância (`StartSecondWorkersAsync`).
"Neutro" ali quer dizer "o que acontece sem o teste pedir nada". O `N = 1` desta
change **não** é mais um desses controles, e **não** é neutro no mesmo sentido: é o
regime de antes da #163, fixado no `BuildWorkersHost` e portanto igual nas três
instâncias que a fixture constrói (subida, reinício, segunda). Os controles da #162
mexem em tempo (LLM, espera pelo lock) e em número de instâncias; o `N` mexe em
quantos slots cada instância tem. São eixos independentes, e a fixture só precisa
de um valor no eixo novo, porque nenhum teste do round-trip pede `N > 1`. **Gatilho
para virar controle:** o primeiro teste do round-trip que precise de `N > 1` na
fixture, e não antes.

**O teste da #162 depende de `N = 1`, e com `N > 1` deixa de significar o mesmo.**
A perna verde (com a serialização do inbox) continua verdadeira com qualquer `N`:
msg2 só vira task depois da primeira terminar, as duas completam. A perna vermelha
(sem a serialização) é que muda. Com `N = 1`, a primeira instância está ocupada, msg2
só pode cair na segunda, que é a do `Command Timeout=3`, e ela falha no lock: é o
`[COMPLETED, FAILED, "aviso de falha", resposta]` medido na #162. Com `N = 4`, a
primeira instância também tem slot livre, e o broker reparte as entregas entre os
dois consumidores; quando msg2 cai na primeira, a espera pelo lock usa o
`Command Timeout` default (30 s), maior que o LLM de 8 s, e a task **completa**. O
guarda passaria verde com o defeito presente sempre que msg2 caísse na primeira
instância. Em que fração das execuções isso aconteceria **não foi medido**: segue da
repartição do broker entre consumidores com prefetch livre, não de um número. Por isso ele entra na tabela acima, e o comentário que afirma
`prefetchCount: 1` (`:38`) passa a dizer que a fixture fixa `N = 1` (tarefa 4.3). O
que o teste afirma sobre o inbox não muda: ele continua sendo o guarda de que o
inbox não despacha msg2 com a primeira em voo, e esse é um fato do inbox, não do
número de slots.

**Guardas novos**, cada um reprovando contra o defeito antes de valer:

1. **Paralelismo com a fila correta.** `N = 2`, três tasks lentas: exatamente duas
   em `Working` e `messages_ready == 1` na fila. **Reprova com prefetch 3** (a
   terceira some da fila sem executar) **e reprova com concorrência 1** (só uma em
   `Working`). É o guarda da pegadinha do construtor (D1).
2. **Delegação numa instância só.** `N = 2`, Source delega para Target, uma
   instância: a delegação conclui com sucesso. Reprova com `N = 1` (expira).
3. **Parada com execuções em voo.** `N = 2`, duas execuções esperando o provedor:
   a parada leva menos de 10 s, os dois jobs voltam à fila, zero locks em posse
   (`pg_locks` com `locktype = 'advisory'`). Reprova na ordem antiga da parada
   (canal fechado antes de `base.StopAsync`, a #49: 30,4 s medidos). **Não reprova
   com o `nack` da parada removido nem com `requeue: false`** (#177, ver D5): ele
   prova o resultado, não o caminho.
4. **`AsyncLocal` sem contaminação.** `N = 2` no **mesmo host**, duas tasks de
   provedores diferentes: cada `provider_calls` tem o provedor da própria task.
   Reprova se o escopo de métricas for promovido de `AsyncLocal` para campo.
5. **Boot fora da faixa.** `N = 0` e `N = 33` reprovam com a mensagem esperada;
   `N = 1` e `N = 32` passam com a linha de sucesso. Teste unitário da extensão,
   como `EmbeddingBatchSizeValidationTests`.

O registro da chamada no `Program.cs` **não é provado por nenhum teste**, pelo mesmo
motivo já escrito lá para `ValidateEmbeddingBatchSize`: os testes montam o host à
mão. É conferência manual de escopo, e o comentário no `Program.cs` diz isso.

### D8. Documentação: corrigir só a parte errada

**Fato de operação, que a documentação não diz: o piloto roda hoje com TRÊS
instâncias de `apps/workers`.** Fonte: o dono, em 18/10/2026, ao abrir o apply desta
change. Contra isso, `docker-compose.prod.yml:15` e `docs/deployment.md:380` dizem
"nunca usar `replicas > 1`", e o serviço `workers` do compose não declara `replicas`.
Nada versionado produz três instâncias, e como elas sobem não está registrado. O que
o repositório dizia antes era "duas" (`02`, entrada da `lock-de-contexto-falha-terminal`,
20/09/2026; proposal e design da #162; `RoundTripFixture.cs:327`), sem registro de
como foi constatado. O `02` ganhou a correção de status nos dois pontos que tratam
disso (tarefa 5.6 completa o resto). Três consequências para esta change:

- **A edição do compose e do §4 continua a mesma** (trocar só o motivo, `replicas`
  continua 1 no arquivo): esta change não decide réplicas (#48), e a divergência
  entre operação e compose não é corrigida aqui. Ela é **escrita** no `02`, como fato.
- **A conta de capacidade de produção passa a ser `3 × N`**, não `N`: com o default
  4, 12 slots no piloto, contra 3 hoje. É isso que a 6.1 (conexões) e o risco do
  gateway (chamadas simultâneas) precisam contar.
- **A docstring "as duas réplicas do piloto" (`RoundTripFixture.cs:327`) fica
  falsa**: passa a dizer que a fixture sobe uma segunda instância para o caso de
  várias instâncias, sem afirmar quantas o piloto tem (tarefa 4.2).

- **`docker-compose.prod.yml` (comentário de Non-Goals) e `docs/deployment.md` §4:**
  a frase atual diz que o lock de `apps/workers`/`apps/inbox` é
  `pg_advisory_lock`/`xmin`, "nada distribuído". **Isso é falso** para os dois
  primeiros (o lock vive no Postgres compartilhado), e é o que o `02` registrou na
  contradição. **O lock em memória do `apps/connectors` (#105) continua valendo**, e a
  parte dele não muda. A edição troca só a justificativa de workers/inbox; a frase
  não é reescrita inteira, e `replicas` continua 1. Cada um dos três passa a ter o
  próprio motivo (requisito renomeado da spec `server-deployment`): `apps/connectors`,
  o lock em memória (#105); `apps/workers`, decisão de disponibilidade em aberto na
  #48; `apps/inbox`, decisão operacional sem restrição técnica de coordenação, a rever
  na #165.
- **`docs/development.md`:** "Delegação entre agentes exige duas ou mais instâncias"
  passa a "exige dois slots": com o default 4, uma instância local basta; com
  `N = 1`, o segundo `dotnet run` continua sendo o caminho.
- **`01` (Filas de trabalho):** `agent-tasks` passa a descrever `N` slots por
  instância, prefetch = concorrência, e o motivo da fila própria de indexação reescrito
  sem afirmar prefetch 1.
- **`docs/configuration.md`:** linha nova na tabela de `apps/workers` e na de
  variáveis do `.env.prod`, com default, faixa, "não medido" e rollback.
- **`02`:** `replicas-de-worker` (#48) passa a tratar de disponibilidade, não de
  capacidade; a fórmula conta slots; a contradição de réplicas é marcada como
  resolvida por esta change; o **Marco 2** da série de métricas passa a ser desta
  change (D9).

### D9. Marco da série de métricas

O deploy desta change muda o regime de duas séries (convenção 22): **tempo de fila**
(uma task deixa de esperar o único slot da instância) e **tempo de lock** (duas
execuções do mesmo contexto no mesmo processo passam a disputar o lock, o que antes
só acontecia entre instâncias). A série se divide pela data do deploy. **Ação:**
registrar no `02`, no dia, a data e o fuso (`America/Sao_Paulo`) e o valor de `N` em
vigor. O Marco 2 que o `02` atribuía à `replicas-de-worker` passa para cá, porque é
aqui que a troca de regime acontece (comentário feito na #48).

**O achado lateral da #162 muda o significado das mesmas duas séries.** Em
`AgentExecutionService`, o `StartWorkAsync` (transição para `working`) vem **antes**
do `ConversationContextLock.AcquireAsync`. A espera pelo lock acontece com a task já
em `working`: em `task_executions` ela fica entre `StartedAt` e `LockAcquiredAt`, e
não no tempo de fila. Daí três coisas:

- **Esta change desloca espera de uma série para a outra.** Com um slot por
  instância, a segunda mensagem do mesmo contexto esperava na **fila**. Com `N`
  slots, ela é consumida, passa a `working` e espera no **lock**. O tempo de fila cai
  e o de lock sobe sem que a conversa fique mais rápida ou mais lenta. Lido sem esse
  aviso, o marco parece ganho de fila e regressão de lock.
- **A #162 move a série de lock no sentido oposto.** Serializar por sessão no inbox
  deixa de despachar a segunda mensagem enquanto a primeira está em voo. A disputa
  pelo lock cai e, com ela, o tempo de lock.
- **A #162 registra o achado no `02` e não o corrige.** A correção é a **#166** (lock
  de contexto adquirido depois do `StartWorkAsync`), change própria, cuja posição
  registrada na #162 (`02`, acréscimo de 18/10/2026 e itens abertos da entrada da
  #162) é **deploy junto desta, um marco só**: as duas mudam o significado das séries
  de tempo de fila e de lock, e num deploy só a quebra das séries é uma. Esta change
  não muda a ordem `StartWorkAsync` → lock; sem a #166, é ela que faz a espera pelo
  lock ser lida como `working`, no painel e nas métricas.

**Dois marcos, em dias diferentes, e o da #162 vem primeiro.**

- **Marco 1: a #162 sozinha.** Já está na `main` (`1e250e6`); o deploy dela (só
  `apps/inbox`) vem antes, e o marco é escrito no `02` no dia dele.
- **Marco 2: esta change e a #166 no mesmo deploy, num dia posterior**, com tráfego
  de produção sob a #162 sozinha no meio, para a série ter um trecho que pertença só
  a ela. Quanto tráfego basta **não é medido**; o mínimo é que esse trecho exista.
  O marco nomeia as duas changes e diz que, dentro dele, os efeitos de cada uma
  **não se separam** na série: esta desloca espera da fila para o lock, a #166 muda
  em que coluna a espera pelo lock é contada. É a troca aceita pela posição da #166:
  um marco com dois efeitos inseparáveis, em vez de dois marcos seguidos de leitura
  frágil.
- **Saída se a #166 não estiver pronta a tempo:** esta change sobe sozinha no marco
  2, com o aviso do deslocamento de espera da fila para o lock escrito acima (a
  ordem `StartWorkAsync` → lock ainda vigente), e a #166 ganha **marco próprio**
  depois, o 3, em outro dia, com o mesmo cuidado de ter tráfego de produção sob o
  marco 2 no meio. **Esta change não espera a #166**: ela não é pré-requisito de
  merge nem de deploy daqui, só de o marco ser um. A decisão de esperar ou não é do
  dono, no deploy (tarefa 6.3).
- **Não fazer:** subir a #162 junto do marco 2. Com as três no mesmo dia, a série de
  lock sofre efeitos opostos (a #162 baixa a disputa, esta a sobe) na mesma data, e
  não se sabe quanto foi de cada uma.

## Árvore de pastas proposta

Só o que muda. Nada em `libs/`.

```
apps/workers/
├── src/Buteco.Workers/
│   ├── Program.cs                              (Configure<TaskConsumerOptions> + ValidateTaskConsumerConcurrency)
│   ├── Options/
│   │   └── TaskConsumerOptions.cs              (novo: SectionName, MaxConcurrentExecutions = 4, teto 32)
│   ├── Messaging/
│   │   ├── TaskJobConsumer.cs                  (CreateChannelOptions + BasicQos com N; comentários)
│   │   └── TaskConsumerConcurrencyValidation.cs (novo: checagem de boot)
│   └── Knowledge/Indexing/
│       └── KnowledgeIndexingConsumer.cs        (só comentário)
└── tests/Buteco.Workers.Tests/
    ├── Messaging/
    │   ├── TaskJobConsumerConcurrencyTests.cs  (novo: guardas 1, 3 e 4; partial de TaskJobConsumerTests, não 15ª classe da coleção)
    │   └── TaskConsumerConcurrencyValidationTests.cs (novo: guarda 5)
    ├── AgentDelegationConcurrencyTests.cs      (N = 1 no BuildHost; guarda 2)
    ├── AgentDelegationExecutionTests.cs        (N = 1 no BuildHost)
    ├── TaskJobConsumerTests.cs                 (partial; N = 1 nos três builders)
    └── ConversationContextLockFailureTests.cs  (N = 1 no BuildHost)
apps/api/src/Buteco.Api/Knowledge/Indexing/
└── KnowledgeIndexingQueues.cs                  (só comentário)
tests/InboxOrchestratorRoundTrip.Tests/Support/
└── RoundTripFixture.cs                         (N = 1)
docker-compose.prod.yml                         (comentário de Non-Goals; TaskConsumer__MaxConcurrentExecutions)
docs/{configuration,deployment,development}.md
01-ARQUITETURA_E_CONVENCOES.md
02-HISTORICO_E_STATUS.md
CHANGELOG.md
```

## Risks / Trade-offs

Cada risco com gatilho observável e posição (convenções 10 e 22).

- **`max_connections` do Postgres** → **não é risco aberto: é tarefa de verificação
  antes do deploy** (tarefa 6.1). O servidor usa o padrão de 100, compartilhado entre
  `apps/api`, `apps/workers` e `apps/inbox` (dois bancos num servidor; o
  `apps/connectors` não fala com o Postgres). Cada pool do Npgsql tem teto 100, então
  a soma teórica já passa de 100 hoje; o que importa é o pico real. A verificação
  mede `pg_stat_activity` por banco e por app no piloto e soma o pior caso de
  `apps/workers` com `N = 4` (D3) **vezes o número de instâncias em operação**,
  que hoje é três (D8): `3 × 4 × 2 = 24` conexões de execução (com a premissa de duas
por execução, a conferir na tarefa 1.3), mais o fixo de cada
  instância. Se não couber, o deploy espera.
- **Pressão no gateway do LLM** → com `N = 4`, cada instância faz até 4× mais
  chamadas simultâneas; com as três instâncias do piloto (D8), até 12 contra 3 hoje.
  **Gatilho:** a taxa de 502 do gateway depois do deploy,
  comparada à de antes. **Resposta:** baixar `N` (D4), sem redeploy de imagem.
- **#145, `CHANNEL_ERROR` derruba as `N`** → com `N` execuções no mesmo canal, um erro
  de canal interrompe todas, e o `StopAsync` envolvido é o mesmo que a D5 mantém.
  **Registrado; corrigir fica na #145.** O raio do defeito passa de 1 para `N`.
- **#126, reentregas mais frequentes** → na parada, `N` jobs voltam à fila em vez de
  1, então as reentregas que reabrem `task_executions` ficam até `N` vezes mais
  frequentes por parada. **Registrado; corrigir fica na #126.**
- **Segunda mensagem do mesmo contato falhando em 30 s** → é o motivo da ordem de
  merge (proposal). **Gatilho de quebra:** mergear antes da #162, já afastado no
  código (a #162 está em `1e250e6`); o que resta é **deployar** antes dela.
  **Posição:** ordem de deploy (D9), não código desta change. Para cliente A2A
  externo no mesmo contexto, o `[Completed, Failed]` continua sendo contrato
  documentado pela #162, e com `N` slots fica mais frequente no mesmo processo.
- **Séries de métricas** → o deploy parte as séries de tempo de fila e de lock pela
  data. **Posição:** marco no `02` (D9).
- **Trade-off aceito:** uma instância passa a concentrar até `N` execuções, então a
  queda de uma instância afeta `N` conversas, não uma. É o que a #48 trata ao decidir
  réplicas por disponibilidade.

## Migration Plan

1. Merge **depois** da #162. Cumprido no código: a #162 está em `1e250e6` e esta
   branch foi rebaseada sobre ela em 18/10/2026. Falta o deploy dela (marco 1, D9).
2. Antes do deploy: tarefa 6.1 (`max_connections`) e a decisão da #166 junto ou não
   (D9, tarefa 6.3).
3. Deploy com o default 4 (sem variável no `.env.prod`, ou com
   `TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS=4` explícito).
4. Conferir no log de boot a linha da checagem com o valor em vigor.
5. Registrar o marco no `02` (D9).
6. Acompanhar a taxa de 502 do gateway.

**Rollback:** `TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS=1` e reinício do `workers`.
Reproduz o comportamento anterior (D4); a delegação volta a expirar numa instância só.

## Open Questions

Nenhuma. Revisar a réplica única do `apps/inbox` virou a **#165**. Até lá, esta
change declara que ela é decisão operacional, sem restrição técnica de coordenação
(D8).
