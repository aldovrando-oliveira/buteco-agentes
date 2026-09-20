## Why

A exploração `replicas-de-worker` (2026-09-20) mediu um defeito **ativo no
piloto**: uma task de `apps/workers` pode ficar presa em `working` para sempre,
sem erro em lugar nenhum, e a mensagem do usuário some sem resposta.

`AgentExecutionService.ExecuteAsync` adquire o `ConversationContextLock`
(`pg_advisory_lock`) **fora** do seu `try` — linha 158, com o `try` começando na
161. `pg_advisory_lock` não tem timeout, mas o `CommandTimeout` do Npgsql (30 s,
default) tem: quando estoura, a exceção sobe de `AcquireAsync` antes do `try`,
escapa de `ExecuteAsync`, e cai no `catch` de `TaskJobConsumer.cs:57-61`, que
loga e faz `BasicNackAsync(requeue: false)`. A task nunca chega a estado
terminal.

Medido por dois caminhos independentes, com Postgres e RabbitMQ reais:

```
Probe_LockAcquisitionTimeout   (lock segurado por fora, 45 s de observação)
  t=1s … t=45s  state=Working          ### estado final: Working
  Falha ao processar mensagem da fila agent-tasks
   ---> System.TimeoutException: Timeout during reading attempt

Probe_SameContextSlowFirst     (2 instâncias, 2 tasks do mesmo (agentId, contextId))
  task 2969a5b3 state=Completed
  task 15fe24e8 state=Working          ← presa para sempre
```

**É alcançável no piloto hoje**, e não era com uma instância. `apps/inbox`
permite duas tasks concorrentes do mesmo `contextId` — o índice único de
`PendingDispatch` filtra por `Status = 'Pending'`
(`apps/inbox/.../Infrastructure/AppDbContext.cs:126-128`, migration
`20260810165646_AddPendingDispatch.cs:44`), então uma linha em `Dispatching` não
impede uma nova `Pending`; um contato que manda a segunda mensagem enquanto o
agente ainda responde a primeira produz exatamente isso. E 30 s de execução não
é caso raro: uma delegação que expira sozinha já consome 120 s. Com **uma**
instância o caminho era inalcançável (`prefetchCount: 1` serializa o consumo
dentro do processo); o piloto roda com **duas**, que é pré-requisito da
delegação. Duas instâncias resolveram a delegação e abriram isto — é a troca que
ninguém tinha escrito.

A cadeia de consequências, verificada no ponto: a tool de delegação que espera
essa task faz polling até o próprio timeout (`IsTerminal` nunca é verdade); o
`PendingDispatch` de `apps/inbox` fica em `Dispatching` para sempre (nada varre
esse estado, e a push notification nunca vem); a mensagem do usuário some sem
erro nenhum.

## What Changes

- **`apps/workers` — a falha ao adquirir o `ConversationContextLock` passa a
  terminar a task como `failed`**, em vez de escapar de `ExecuteAsync`. O valor
  vem do efeito em cadeia: com a task em `failed`, a push notification dispara
  pelo caminho que já existe, e o `PendingDispatch` de `apps/inbox` é resolvido
  sem nenhuma mudança naquele app.
- **`apps/workers` — `ConversationContextLock.AcquireAsync` passa a descartar o
  `IServiceScope` quando a aquisição falha.** Hoje o escopo (e a conexão
  Postgres já aberta) vazam nesse caminho, porque o objeto que os possui nunca
  chega a ser construído. Enquanto o caminho era uma exceção não tratada isso
  era um vazamento por acidente raro; esta change o torna um caminho de
  operação rotineiro, e um vazamento por ocorrência esgotaria o pool. Entra aqui
  por ser consequência direta da própria correção, não por oportunidade — ver
  `design.md`, D3.
- **`apps/workers` — dois registros de mecanismo em `ConversationContextLock`**,
  achados pela exploração e sem defeito a corrigir, mas que se perdem se não
  ficarem onde quem investiga vai olhar: (1) o Npgsql **não** libera advisory
  lock ao devolver a conexão ao pool — medido, `[4] depois de Close(), o lock
  ainda existe? True` —, então o desenho depende do unlock explícito de
  `DisposeAsync` e nada no arquivo dizia isso; (2) `hashtext` pode colidir, e
  uma colisão serializa dois pares `(agente, contexto)` não relacionados —
  degradação silenciosa, nunca corrupção.
- **Quatro guardas entram como teste de verdade** (convenção 15), dois
  restaurados da sonda descartável da exploração e dois novos, cada um prendendo
  uma propriedade distinta:
  1. **Aquisição que estoura termina a task em `failed`** — o defeito
     diretamente, restaurado de `Probe_LockAcquisitionTimeout`.
  2. **Duas instâncias, duas tasks do mesmo `(agentId, contextId)`, a primeira
     lenta: as duas terminam** — o caminho alcançável em produção, restaurado de
     `Probe_SameContextSlowFirst`.
  3. **Task `completed` não é sobrescrita quando a liberação do lock falha** —
     prende a alternativa recusada em `design.md` D1. Sem ele, nada impede
     alguém refazer a correção movendo o `await using` para dentro do `try`, e a
     consequência medida é uma task concluída com sucesso sobrescrita como
     `failed`, com a resposta do agente perdida junto.
  4. **Falhas repetidas de aquisição não deixam o processo inutilizável** — o
     guarda do descarte de escopo. Afirma consequência, não implementação: sem
     o descarte, o pool esgota e o worker não consegue nem gravar as próprias
     falhas, e as tasks voltam a ficar presas em `working` pelo outro motivo.

  As duas correções são independentes e entram **em ordem**, com uma rodada de
  guardas no estado intermediário — senão o descarte de escopo entraria sem
  nunca ter tido um guarda vermelho contra o seu próprio defeito (`tasks.md`,
  seção 2).

**Não é BREAKING.** Nenhum contrato de API, formato de fio ou schema muda. O
estado terminal `failed` já é produzido por esta mesma classe em todos os outros
caminhos de falha, e `apps/api`/`apps/inbox` já o tratam.

## Capabilities

### New Capabilities

Nenhuma. O defeito é a violação de uma propriedade que a spec viva já afirma —
`a2a-task-lifecycle`, cenário "Falha na execução do agente leva a task a failed:
… **sem deixar a task presa indefinidamente em `working`**" — cujo escopo hoje
cobre só a falha da chamada ao LLM.

### Modified Capabilities

- `a2a-task-lifecycle`: o requisito "Workers processam a task até um estado
  terminal" passa a nomear a serialização por `(agentId, contextId)` entre
  instâncias concorrentes (hoje só existe em código e no `design.md` de uma
  change arquivada) e a afirmar que **a falha em adquirir essa serialização
  também leva a task a um estado terminal**, não só a falha da chamada ao LLM.

## Impact

**Código afetado — `apps/workers` apenas.** Nenhuma referência de projeto nova,
nenhum app além deste.

- `apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs` — aquisição
  do lock passa a ter tratamento próprio de falha.
- `apps/workers/src/Buteco.Workers/Agents/ConversationContextLock.cs` — descarte
  do escopo na falha de aquisição, mais os dois registros de mecanismo.
- `apps/workers/tests/Buteco.Workers.Tests/` — guardas novos, no molde de
  `AgentDelegationConcurrencyTests` (Testcontainers `pgvector/pgvector:pg18` +
  `rabbitmq:4.3-management`).

**Nada em `apps/api`, `apps/inbox`, `apps/frontend`, `libs/`, compose,
migrations ou documentação de deploy.**

**Superfície que esta change deliberadamente NÃO toca**, e o motivo medido: o
limite de espera continua sendo o `CommandTimeout` default do Npgsql (30 s), sem
virar constante nova nem opção configurável. Escolher um valor exige saber
quanto tempo uma conversa legitimamente segura o lock, que é a métrica de
operação que não existe (V6 da exploração) — e afirmar um limite cujo mecanismo
não foi estabelecido é o defeito que esta change corrige, na forma oposta
(convenção 13). O que muda é o limite passar a estar **escrito** e a ter guarda,
não a ter um valor novo. Os testes encurtam o limite pela connection string do
próprio host de teste (`Command Timeout=N`), verificado produzir a mesma exceção
em 3,2 s — sem knob de produção e sem tocar nenhum dos 13 sítios de teste que
instanciam `AgentExecutionService` (12 em `apps/workers/tests/`, 1 em
`tests/InboxOrchestratorRoundTrip.Tests/`; ver `design.md`, D4, para a régua com
escopo).

**Non-Goals explícitos:**

- **Não mexer em número de instâncias, no compose nem na documentação de
  deploy.** Trocar "nunca mais de uma" por "no mínimo duas" repetiria a forma do
  defeito no sentido oposto: a medição contradiz "duas bastam" para `C ≥ 2`.
  Essa frase entra na change de réplicas, com a fórmula `N ≥ C × D + 1`.
- **Não corrigir o ciclo `A→B→A`** (change própria, a seguinte na fila).
- **Não instrumentar diagnóstico de delegação** (change própria).
- **Não tocar métricas.**
- **Não varrer `PendingDispatch` órfã em `Dispatching`** — esta change remove o
  vazamento que hoje alimenta esse estado, mas não o estado residual nem as
  linhas já presas no piloto. Decisão e gatilho em `design.md`, D5.
