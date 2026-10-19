**Issue:** #163
**Relacionadas:** #48, #162 (pré-requisito de merge, já na `main`), #166 (deploy junto, D9)

## Why

Cada instância de `apps/workers` executa **uma task por vez**
(`TaskJobConsumer.cs:52`, `prefetchCount: 1`). A execução é quase toda espera de
I/O (LLM, MCP, Postgres, polling de delegação), então várias execuções no mesmo
processo custam pouco, e escalar réplicas só para ganhar vazão é o caminho caro.

E há uma contradição funcional registrada no `02`, ainda aberta: o compose de
produção proíbe `replicas > 1` para `apps/workers`, e a delegação precisa de dois
"lugares" para progredir. Com uma instância só e `prefetchCount: 1`, toda
delegação expira pelo timeout de 120 s. Esta change troca a unidade de capacidade
de **instância** para **slot**: com `N` execuções por instância, a conta passa a
ser `réplicas × N ≥ C × D + 1`, e uma instância só já fecha uma delegação.

## What Changes

- `TaskJobConsumer` passa a executar até `N` tasks em paralelo no mesmo processo.
  `N` vem de **uma** opção nova, `TaskConsumer:MaxConcurrentExecutions`, que define
  **ao mesmo tempo** o `prefetchCount` do `BasicQos` e o
  `ConsumerDispatchConcurrency` do canal de tasks. Prefetch e concorrência nunca
  divergem: prefetch maior que a concorrência prende entregas no processo, fora do
  alcance de outras instâncias, e reabre o autodeadlock da delegação mesmo com
  várias réplicas.
- **Default 4, declarado não medido**, com gatilho de recalibração no `C` de pico
  (convenção 22). Calibrar fica na #48.
- **Validação no boot** (convenção 8): `N` fora de `[1, 32]` reprova o boot, sem
  correção automática; se passa, uma linha de log com o valor. O teto de 32 vem do
  pool do Npgsql (100 conexões): cada execução segura uma conexão dedicada ao lock
  de contexto.
- **Rollback declarado:** `N = 1` reproduz exatamente o comportamento atual.
- A sequência de parada não muda (`BasicCancel` → `base.StopAsync` →
  `nack(requeue: true)`). Com `N` execuções em voo, o cancelamento é paralelo e a
  parada continua cabendo nos 10 s do compose.
- Testes que dependem de `N = 1` passam a fixá-lo no harness, com o motivo escrito.
  Cinco guardas novos (paralelismo com `messages_ready` correto, delegação numa
  instância só, parada com execuções em voo, `AsyncLocal` sem contaminação, boot
  fora da faixa).
- Documentação: corrigir **só** a parte errada do Non-Goal do
  `docker-compose.prod.yml` e do `docs/deployment.md` (`pg_advisory_lock`/`xmin` são
  distribuídos; o lock em memória do `apps/connectors`, #105, continua valendo);
  `docs/development.md` ("dois slots", não "duas instâncias"); `01` (Filas de
  trabalho); `docs/configuration.md` (a opção nova); `02` (a `replicas-de-worker`,
  #48, passa a tratar de disponibilidade, e o marco da série de métricas).

**Fora desta change:** `KnowledgeIndexingConsumer` continua com 1 (conexão e
factory próprias, teto controlado na entrada da fila). Calibrar `N` e decidir o
número de réplicas continua na #48. Atualizar o `RabbitMQ.Client` para 7.2.2 fica
fora (não mexe no dispatcher).

## Ordem em relação à `inbox-serializacao-por-sessao` (#162)

**Desenvolvimento em paralelo, merge na `main` só DEPOIS da #162.** Não há
dependência de código entre as duas. A dependência é de comportamento: com o
default 4, duas mensagens do mesmo contato passam a cair em execuções simultâneas
com frequência muito maior, e a `main` sem o inbox novo falha em 30 s a segunda
mensagem (lock de contexto) muito mais vezes do que hoje.

**Cumprido no código em 18/10/2026:** a #162 mergeou (`1e250e6`, PR #174) antes de
esta começar o apply, e esta branch foi rebaseada sobre ela (fast-forward, sem
conflito, porque ainda não havia código). As edições desta em
`ConversationContextLockFailureTests.cs` e em `tests/InboxOrchestratorRoundTrip.Tests`
partem do que a #162 deixou (D7). O que resta da ordem é de **deploy**: a #162 sobe
sozinha antes (marco 1 da D9).

## Ordem em relação à #166 (lock depois do `StartWorkAsync`)

A **#166** corrige a ordem `StartWorkAsync` → lock em `AgentExecutionService`, achada
pela #162. A posição registrada na #162 é **deploy junto desta, um marco só** nas
séries de tempo de fila e de lock. Esta change não depende da #166 nem para merge nem
para deploy: se a #166 não estiver pronta a tempo, esta sobe sozinha e a #166 ganha
marco próprio depois (D9).

## Capabilities

### New Capabilities

- `workers-concurrent-task-consumption`: quantas tasks uma instância de
  `apps/workers` executa ao mesmo tempo, a opção única que fixa prefetch e
  concorrência juntos, o default, a faixa válida checada no boot e a equivalência
  de `N = 1` com o comportamento anterior.

### Modified Capabilities

- `agent-delegation-execution`: o requisito "Processamento concorrente de Source e
  Target entre réplicas" passa a contar **slots**, não réplicas. A delegação
  progride numa instância única com `N ≥ 2`, e expira pelo timeout quando não há
  slot livre (o caso de `N = 1` com uma instância).
- `a2a-task-lifecycle`: o requisito "Parada do worker com execução em voo" passa a
  cobrir **várias** execuções em voo: todas canceladas antes do fechamento do
  canal, todos os jobs devolvidos à fila, nenhum lock preso.
- `agent-execution-metrics`: "Execuções concorrentes não misturam métricas" ganha o
  cenário de duas execuções no **mesmo host**, que é o caso de produção desta
  change e não só o de teste.
- `server-deployment`: "Serviços com coordenação em memória rodam em réplica única"
  é renomeado para "Serviços de réplica única declaram o motivo de cada um", e deixa
  de dar, para `apps/workers` e `apps/inbox`, um motivo de coordenação que é falso.
  `apps/workers`: decisão de disponibilidade, em aberto na #48. `apps/inbox`: decisão
  operacional, sem restrição técnica de coordenação, a rever na #165. O motivo do
  `apps/connectors` (#105) não muda.

## Impact

- **`apps/workers`** (código): `Messaging/TaskJobConsumer.cs`, opção nova em
  `Options/`, checagem de boot nova e a chamada dela no `Program.cs`.
- **`apps/workers`** (testes): os harnesses que dependem de `N = 1` (lista no
  `design.md`) e os cinco guardas novos.
- **`tests/InboxOrchestratorRoundTrip.Tests`**: `RoundTripFixture` fixa `N = 1`.
- **`apps/api`**: só o comentário de `KnowledgeIndexingQueues.cs`, que afirma
  `prefetchCount: 1` do consumidor de tasks. Nenhum código muda, e nenhuma
  referência de projeto entre apps é criada.
- **Documentação:** `docker-compose.prod.yml` (comentário), `docs/deployment.md`,
  `docs/development.md`, `docs/configuration.md`, `01`, `02`.
- **Operação:** antes do deploy, verificar o `max_connections` do Postgres contra a
  soma dos pools dos apps que falam com o servidor. O deploy parte as séries de
  tempo de fila e de lock pela data (marco no `02`).
- **Dependências:** nenhuma nova. `RabbitMQ.Client` continua em 7.2.1.
