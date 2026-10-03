**Issue:** #49

## Why

`TaskJobConsumer.StopAsync` fecha o canal e a conexão do RabbitMQ **antes** de
chamar `base.StopAsync`, que é quem cancela o `stoppingToken` da execução
(`apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs:69-84`). No
`RabbitMQ.Client` 7.2.1, `Channel.CloseAsync` espera o callback do consumidor
terminar; o callback espera um cancelamento que ainda não veio. Reproduzido em
03/10/2026 sobre `f2e7b09`: com uma execução em voo, `host.StopAsync()` levou
**60,1 s** e lançou `TaskCanceledException`, e a escrita final encontrou o
`IServiceProvider` descartado. Em produção o Compose não define
`stop_grace_period`, e o padrão de **10 s** — lido na documentação do Compose,
não medido aqui — mata o processo bem antes disso (o `ENTRYPOINT` do
`apps/workers` está em forma exec, então o `SIGTERM` chega ao processo).

A #49 estava posicionada dentro da `replicas-de-worker`; o dono a antecipou para
a trilha paralela em 03/10/2026 (comentário na issue, convenção 22).

## What Changes

- **`apps/workers`, parada do consumidor:** `StopAsync` passa a, nesta ordem,
  cancelar o consumidor no broker (`BasicCancelAsync`), cancelar a execução em voo
  (`base.StopAsync`), e só então fechar canal e conexão. A parada deixa de esperar
  os dois prazos de fechamento (30 s do host + 30 s da conexão) e a execução em
  voo termina com o host ainda vivo. O cancelamento no broker vem primeiro porque,
  sem ele, o job devolvido é reentregue ao próprio consumidor em laço até o canal
  fechar — medido: **649 reentregas em 2 s** com a janela aberta (`design.md`, D5).
- **`apps/workers`, execução interrompida por parada** (opção B do D2, **aprovada
  pelo dono em 03/10/2026**): a task fica em `working`, nenhuma escrita de
  `failed` é tentada, e o job volta à fila por um `nack` com `requeue: true`
  explícito. A reentrega cai no caminho que já existe ("task em `working` continua
  executando"). A parada é registrada em log como informação, não como erro.
- **Teste de reprodução** em `TaskJobConsumerTests`, que reprova sobre o código
  atual (60,1 s, medido) e é o guarda da correção.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `a2a-task-lifecycle`: acrescenta o requisito de que a parada do worker com uma
  execução em voo para de receber jobs, cancela a execução antes de fechar a
  conexão com o broker, retorna sem esperar os prazos de fechamento, e deixa a
  task em `working` com o job devolvido à fila, sem reentregá-lo ao worker que
  está parando. O requisito "Task lida em estado terminal não é executada
  de novo" não muda e é o que a reentrega usa.

## Impact

- **Código:** `apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs` e o
  tratamento de cancelamento em
  `apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`. Nada fora de
  `apps/workers`, nada em `libs/`.
- **Testes:** casos novos em `TaskJobConsumerTests`, sem classe de host nova — a
  contagem da `WorkerHostCollection` não muda e a recalibração dela não é
  disparada (convenção 22).
- **Fora do escopo, explícito:** `PushNotificationSender` e a configuração dos
  `HttpClient` de saída (território da #46 e da #47, que vêm em seguida nesta
  trilha); `KnowledgeIndexingConsumer`, que tem o mesmo padrão e virou a **#125**;
  o ruído de log da reabertura de `task_executions` na reentrega, medido no apply,
  que virou a **#126**;
  `stop_grace_period` do `docker-compose.prod.yml` (deploy); `apps/api`,
  `apps/inbox`, frontend.
- **Não afirma:** que o "1m10s em `Connection.CloseAsync`" da
  `delegacao-diagnostico` era este defeito. Continua hipótese nomeada; os dados
  daquela rodada não existem.
