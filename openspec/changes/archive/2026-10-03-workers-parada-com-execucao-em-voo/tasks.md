> **Trilha paralela.** Esta change roda no worktree `../buteco-agentes-49`, branch
> `fix/49-stopasync-cancelamento`, a partir de `f2e7b09`. A linha principal (#103)
> edita `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`; edições curtas e em entrada
> nova. **Antes de cada execução da suíte:** `podman ps` vazio (critério da
> `WorkerHostCollection`) e `uptime` registrado na mesma frase do número; rodada
> acima de ~10 min não é medição. Variáveis: `DOCKER_HOST` do Podman,
> `TESTCONTAINERS_RYUK_DISABLED=true`, `TZ=America/Sao_Paulo`,
> `DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
>
> **D2 decidido pelo dono em 03/10/2026: opção B**, com o cancelamento do
> consumidor no broker primeiro (D5) e o advisory lock conferido no caminho
> cancelado (D6).

## 1. Conferência antes do código

- [x] 1.1 Baseline em worktree limpa (convenção 19): `dotnet test
  apps/workers/Workers.sln` em `f2e7b09`, saída inteira e `.trx` em
  `~/buteco-runs/49/baseline-f2e7b09.*`. **386/386, 39 classes / 39 arquivos
  `*Tests.cs`, 477 s**, `podman ps` vazio, load 4,61 em 12 núcleos na largada.
  Contagem por classe em `baseline-f2e7b09.classes.txt`.
- [x] 1.2 `RabbitMQ.Client` 7.2.1 descompilado: `Channel.CloseAsync` espera o
  worker do dispatcher (`WaitForShutdownAsync`) até o token do host;
  `Connection.CloseAsync` usa `DefaultConnectionCloseTimeout` = 30 s.
- [x] 1.3 Reprodução por execução: o teste
  `StopAsync_WithExecutionInFlight_CancelsItAndReturnsPromptly` reprova sobre
  `f2e7b09` — 60,1 s com `ShutdownTimeout` padrão, 40,0 s com 10 s — e passa no
  protótipo do D1 (0,1 s). Saídas em `~/buteco-runs/49/repro-*.txt` e
  `prototipo-A-reordena.txt`.
- [x] 1.4 Varredura de escopo: 4 hosted services conferidos item a item; o
  `KnowledgeIndexingConsumer` virou a #125.
- [x] 1.5 Laço de reentrega da B, por execução (D5): sem espera artificial, 1
  entrega em 10 de 10 rodadas; com 2 s entre `base.StopAsync` e o fechamento,
  **649** entregas do mesmo job; com `BasicCancelAsync` primeiro, **1**; com guarda
  na entrada do callback, **2**. Saídas em `~/buteco-runs/49/prototipo-B-*.txt`.
  Protótipos revertidos.
- [x] 1.6 Advisory lock no caminho cancelado (D6): `pg_advisory_unlock` com
  `CancellationToken.None` (`ConversationContextLock.cs:132-136`); `pg_locks` com
  zero locks do contexto depois da parada nas 13 rodadas do protótipo. Sem
  comentário na #46.

## 2. Testes primeiro

- [x] 2.1 `TaskJobConsumerTests`: o teste de reprodução continua como guarda de
  tempo — `StopAsync` sem exceção e abaixo de 10 s com a chamada ao provedor em
  voo.
- [x] 2.2 `TaskJobConsumerTests`, mesmo cenário, estado da B com contagens
  exatas: client falso chamado **uma** vez; **um** log de informação de
  interrupção pela parada; **uma** linha em `task_executions` para a task e
  **nenhum** aviso de falha de abertura; `a2a_tasks` em `Working`, nenhum `Failed`;
  **uma** mensagem na fila depois da parada; **nenhum** log `>= Error`. Usar o
  `CapturingLoggerProvider` que a classe já tem (o `BuildHost` passa a aceitar a
  lista). Esvaziar a fila no fim.
- [x] 2.3 `TaskJobConsumerTests`, guarda do laço (D5), determinístico: no
  instante em que a execução observa o cancelamento, consultar a fila por
  `QueueDeclarePassiveAsync` numa conexão própria e afirmar `ConsumerCount == 0`.
- [x] 2.4 `TaskJobConsumerTests`: caso da **tool** em voo (D4) — o client falso
  pede uma chamada de função na primeira resposta, e a função espera pelo token
  que recebe; parar o host com ela em voo. Asserções de 2.1 e 2.2, e o client
  **não** recebe uma segunda chamada com o resultado da tool.
- [x] 2.5 `TaskJobConsumerTests`, reentrega depois da parada, com o lock nas duas
  pontas (D6): antes da parada, `pg_locks` tem **1** advisory lock do par
  `(hashtext(agentId), hashtext(contextId))` (precondição — sem ela o zero não
  prova liberação); entre a parada e a reentrega, **0**; então um host novo, no
  mesmo processo e portanto no mesmo pool do Npgsql, com client que responde,
  consome o job devolvido e a task chega a `Completed`. Registrar o que
  `task_executions` e o log mostram para a linha reaberta (risco "Reentrega abre
  a linha de métrica de novo"); se o aviso de abertura for ruído novo, abrir issue
  na hora com os rótulos da taxonomia.
- [x] 2.6 Provar que cada guarda reprova contra o defeito que ele prende
  (convenção 15), e registrar: contra o código atual, 2.1 reprova (tempo) e 2.2,
  2.4 e 2.5 reprovam; contra o protótipo só com o D1 (opção A), 2.2 reprova pelos
  logs de erro; contra a B **sem** `BasicCancelAsync`, 2.3 reprova.

## 3. Implementação

- [x] 3.1 `TaskJobConsumer`: guardar o `consumerTag` devolvido por
  `BasicConsumeAsync`.
- [x] 3.2 `TaskJobConsumer.StopAsync`, nesta ordem: `BasicCancelAsync` do
  consumidor com **prazo próprio de 2 s** (não o token de `StopAsync`; estouro ou
  falha vira aviso e não impede o resto — D5, acréscimo do dono no ok do apply),
  `base.StopAsync`,
  `_channel.CloseAsync`, `_connection.CloseAsync`. Comentário do mecanismo medido
  (os dois prazos de 30 s, a espera do dispatcher no 7.2.1, o laço de 649
  entregas) e da #49.
- [x] 3.3 `TaskJobConsumer`, callback: `catch (OperationCanceledException) when
  (stoppingToken.IsCancellationRequested)` antes do `catch` geral, com
  `BasicNackAsync(requeue: true, CancellationToken.None)` e log de informação.
  O `catch` geral fica como está.
- [x] 3.4 `AgentExecutionService`: nos dois `catch` que chamam `FailTaskAsync`
  (aquisição do lock e execução), filtro que deixa passar o
  `OperationCanceledException` quando o token da execução foi cancelado. O
  `finally` da métrica não muda.
- [x] 3.5 Rodar 2.1–2.5 com a correção: todos passam.

## 4. Suíte

- [x] 4.1 `dotnet test apps/workers/Workers.sln` com a correção, saída inteira e
  `.trx` em `~/buteco-runs/49/fechamento-<commit>.*`, com `podman ps` e `uptime`
  na mesma frase. Comparar com a baseline **por classe** (`trx.py`), não só no
  total: só `TaskJobConsumerTests` pode mudar de contagem; duração por classe
  comparada.
  **Medido em 03/10/2026, árvore de trabalho sobre `f2e7b09` (sem commit — o commit
  é do dono), saída em `~/buteco-runs/49/fechamento-workdir.*`: 391/391, 39
  classes, 391 s**, com `podman ps` vazio e load 5,07 em 12 núcleos na largada (2,83
  no fim). A primeira tentativa foi abortada antes de começar: a linha principal
  rodava as suítes dela na mesma VM do Podman (load 13,97, contêineres de pé).
  Contra a baseline (386/386, 477 s): só `TaskJobConsumerTests` mudou de contagem,
  **18 → 23** (os 5 guardas), 7,9 s → 9,3 s. Única variação de duração acima de 5 s:
  `ConversationHistoryTests` 45,7 s → 33,5 s, mais rápida, sem mudança nela.

## 5. Fechamento

- [x] 5.1 `/opsx:sync`; conferir `openspec/specs/a2a-task-lifecycle/spec.md`
  requisito a requisito. Se a sync criar `Purpose` placeholder, escrever o texto.
  Conferido: um requisito novo ("Parada do worker com execução em voo", 5
  cenários), inserido depois de "Task lida em estado terminal não é executada de
  novo"; nenhum outro requisito tocado (+40/−0); `Purpose` já era texto real.
- [x] 5.2 `/opsx:archive`.
- [x] 5.3 Entrada no `02-HISTORICO_E_STATUS.md`: o que mudou, baseline e número
  final, a decisão do D2 com o motivo e as condições do dono (D5, D6) — com as 10
  rodadas limpas contra as 649 voltas com a janela aberta, e por que o guarda do
  2.3 é `ConsumerCount == 0` e não a contagem de entregas —, a #125, a
  recalibração de posição da #49 (03/10/2026) e o "1m10s" mantido como hipótese
  não confirmada. Atualizar o `CHANGELOG.md`.
- [x] 5.4 `openspec validate --all` e `scripts/check-docs.py` limpos.
- [x] 5.5 Parar e reportar. Push e PR com `Closes #49` (e `Refs #125`) são do dono.
