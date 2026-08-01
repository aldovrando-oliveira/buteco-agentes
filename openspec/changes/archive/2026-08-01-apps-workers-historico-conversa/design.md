## Context

`AgentExecutionService.ExecuteAsync` (`apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`)
chama sempre `aiAgent.RunAsync(userText, session: null, options: null, cancellationToken)`
(linha 67) e `ExtractLatestUserText` só lê o `History` da `AgentTask` **atual**
(uma task = uma troca). Não existe, hoje, nenhum código que olhe para tasks
anteriores do mesmo `contextId`. Resultado: um segundo `SendMessage` no mesmo
`contextId` gera uma task nova processada pelo LLM sem nenhum conhecimento da
troca anterior, mesmo com a task anterior `completed`.

Investigação feita antes de qualquer decisão abaixo (via `/opsx:explore`,
decompilando os pacotes reais instalados com `ilspycmd` — não assumido de
memória de treinamento):

**Versões já fixadas em `Directory.Packages.props`, confirmadas suficientes**
(nenhum bump necessário):
- `Microsoft.Agents.AI` 1.15.0 (`apps/workers`).
- `Microsoft.Extensions.AI.OpenAI` 10.8.1 resolve `Microsoft.Extensions.AI`
  transitivamente em **10.6.0** (confirmado via `*.deps.json`/`project.assets.json`
  do `Buteco.Workers` já publicado) — `MessageCountingChatReducer` (usado na
  Decisão 4) já existe nessa versão (confirmado via XML doc do pacote
  instalado).

**API real do Microsoft Agent Framework (não é a que a literatura antiga
chama de `AgentThread`)**: na versão 1.15.0 instalada, o conceito de "thread
de conversa" chama-se `AgentSession` — a documentação do próprio tipo diz
literalmente "Base abstraction for all agent threads". Métodos relevantes em
`AIAgent`: `CreateSessionAsync()`, `SerializeSessionAsync(session) →
JsonElement`, `DeserializeSessionAsync(JsonElement) → AgentSession` — a
doc-comment de `SerializeSessionAsync` diz explicitamente que existe "to
support conversations that may need to survive application restarts or be
migrated between different agent instances", com aviso de segurança para
tratar o blob serializado como dado sensível. `ChatClientAgent` (usado hoje em
`AgentExecutionService`) usa por padrão `InMemoryChatHistoryProvider`
(confirmado no código decompilado, `ChatHistoryProvider = options?.ChatHistoryProvider
?? new InMemoryChatHistoryProvider(null)`), que guarda a lista de mensagens
dentro de `AgentSession.StateBag` — uniforme entre os três providers hoje
suportados (`ChatClientResolver`: OpenAI via Chat Completions, Anthropic,
Gemini — todos clients "stateless" de lista de mensagens, nenhum usa
conversation id gerenciado pelo serviço), então não há um caminho especial por
provider a considerar.

**Quem aplica migration neste repo, na prática**: `README.md` só documenta
`cd apps/api/src/Buteco.Api && dotnet ef database update` — `apps/workers`
mantém seu próprio modelo/migrations EF (mesmas tabelas de `apps/api`, mesmo
Postgres) só por simetria; suas migrations só rodam de fato dentro do fixture
de teste isolado do próprio worker (`WorkerInfrastructureFixture`), nunca
contra o Postgres real. Isso é relevante porque a primeira ideia avaliada para
persistir a sessão (tabela nova, só em `apps/workers`) esbarraria nisso — ver
Decisão 1.

**Prior art direto**: `openspec/changes/archive/2026-07-26-backend-agente-a2a-mvp/design.md`,
Decisões 3, 4 e 8 — a tabela `a2a_tasks` (`payload` jsonb) já é escrita por
duas implementações independentes de `PostgresTaskStore` (uma em `apps/api`,
uma em `apps/workers`), com `tests/CrossAppTaskStoreCompatibility.Tests` como
único mecanismo de verificação de compatibilidade de schema entre elas.

## Goals / Non-Goals

**Goals:**
- `apps/workers` incluir o histórico de conversa do mesmo `contextId` em toda
  chamada ao LLM.
- Limitar o crescimento do histórico por padrão (constante global, não por
  agente).
- Excluir turnos `failed`/`rejected` do histórico reconstruído.
- Garantir que editar as `Instructions` de um agente no meio de uma conversa
  em andamento não fique "preso" a um valor antigo dentro da sessão
  persistida.
- Cobrir o comportamento com teste de integração (`IChatClient` mockado,
  incluindo prova explícita de ausência de estado em memória entre
  instâncias do worker) e estender `tests/CrossAppTaskStoreCompatibility.Tests`
  para o novo formato de dado lido entre as duas implementações de
  `ITaskStore`.

**Non-Goals:**
- UI para visualizar/gerenciar histórico de conversa.
- Qualquer mudança de **comportamento** em `apps/api` — o agrupamento por
  `contextId` no protocolo A2A já funciona; `apps/api` nunca lê nem escreve o
  campo novo introduzido aqui (ver Decisão 1). Única exceção, explícita: um
  comentário de documentação sem mudança de comportamento (ver Decisão 2).
- Configuração de limite de histórico por agente (via interface/coluna) —
  nasce como constante global nesta fatia.
- Resolver de forma geral corridas de concorrência entre instâncias do worker
  fora do escopo específico de "mesmo `contextId`" (ver Decisão 7).

## Decisions

### 1. Sessão nativa (`AgentSession`) serializada dentro de `AgentTask.Metadata` — sem tabela nova

`AgentTask` (tipo público do pacote `A2A`, o mesmo persistido em
`a2a_tasks.payload`) já tem um campo de extensão previsto pelo protocolo:
`Dictionary<string, JsonElement>? Metadata`. Decisão: depois de uma execução
bem-sucedida, gravar `task.Metadata["conversationSession"] =
await aiAgent.SerializeSessionAsync(session)` antes do `SaveTaskAsync` final
(o mesmo que já acontece hoje). Ao processar a próxima mensagem do mesmo
`contextId`, ler esse campo da task `completed` mais recente e chamar
`aiAgent.DeserializeSessionAsync(blob)` para obter o `AgentSession` a passar
para `RunAsync`. Confirmado via `TaskProjection.Apply` (decompilado) que a
projeção de eventos do SDK nunca toca em `Metadata` — só `Status`/`History`/
`Artifacts` — então atribuir `Metadata` diretamente no objeto projetado, logo
antes do `SaveTaskAsync`, é seguro.

Isso combina o que o framework já resolve nativamente (serialização de sessão,
reducer de histórico — Decisão 4, exclusão de turnos falhos "de graça" —
Decisão 6) sem exigir nenhuma tabela/coluna nova.

**Se `DeserializeSessionAsync` falhar** (blob corrompido, ou serializado por
uma versão incompatível do pacote `Microsoft.Agents.AI` após um bump futuro):
a exceção propaga para o `catch` genérico já existente em
`AgentExecutionService.ExecuteAsync`, levando a task a `failed` — o mesmo
padrão já usado em todo o resto do método (ex. `IChatClientResolver.Resolve`
lançando para provider não configurado). Nenhum tratamento especial é
necessário; documentado aqui para deixar explícito o que já seria o
comportamento natural, em vez de deixar implícito.

**Alternativas consideradas**:
- **Reconstrução manual da lista de `ChatMessage` a partir de várias tasks
  `completed`** (via `ListTasksAsync(contextId)`, já existente e hoje sem
  nenhum consumidor). Viável e mais simples de depurar linha a linha (nada de
  blob serializado opaco), mas reimplementa o que `ChatClientAgent` +
  `InMemoryChatHistoryProvider` já fazem (acumulação, reducer, notificação de
  falha) — mais código próprio para manter, sem ganho real. Rejeitada em favor
  da sessão nativa.
- **`AgentSession` com tabela dedicada nova** (ex. `agent_sessions`, chave
  `(agent_id, context_id)`), abordagem "Option 1 pura" cogitada inicialmente.
  Tecnicamente mais simples de raciocinar (uma tabela própria em vez de um
  campo dentro de um blob existente), mas esbarra no fato de que só
  `apps/api` aplica migration contra o Postgres real hoje (ver Context): uma
  tabela exclusiva de `apps/workers` exigiria (a) `apps/api` ganhar uma
  migration para uma tabela que nunca usa — na prática uma mudança em
  `apps/api`, o que fere o non-goal explícito — ou (b) mudar o processo de
  deploy para `apps/workers` também rodar sua própria migration pela primeira
  vez, quebrando a convenção estabelecida sem necessidade real. Rejeitada:
  reaproveitar `Metadata` dentro do `payload` já existente elimina esse
  problema inteiro sem abrir mão de nenhum benefício da sessão nativa.

### 2. Escopo da consulta: `ListTasksAsync` do `PostgresTaskStore` de `apps/workers` passa a filtrar por `AgentId`

Achado durante a investigação: `PostgresTaskStore.ListTasksAsync` (nas duas
implementações, `apps/api` e `apps/workers`) filtra a consulta só por
`ContextId`/`Status`, mas **nunca** por `AgentId` — apesar de
`A2ATaskRecord.AgentId` existir e ser gravado em toda `SaveTaskAsync`. Hoje
isso é inofensivo porque `ListTasksAsync` não tem nenhum consumidor; a partir
desta mudança, `apps/workers` passa a depender dele para localizar "a última
task `completed` deste `contextId`" — sem o filtro por agente, um `contextId`
teoricamente reusado por dois agentes diferentes leria a sessão errada.
Decisão: adicionar `.Where(task => task.AgentId == agentId)` à consulta da
cópia de `apps/workers` (usando o `agentId` com que o `PostgresTaskStore` já é
construído hoje — `new PostgresTaskStore(scopeFactory, message.AgentId)`),
igual ao que `SaveTaskAsync` já implicitamente garante ao gravar.

**A cópia de `apps/api` não é alterada em comportamento** (non-goal) — como
cada app mantém sua própria implementação independente (Decisão 3 do
design.md de `backend-agente-a2a-mvp`), corrigir só a de `apps/workers` não
fere isolamento nem exige coordenação. Por pedido explícito, a cópia de
`apps/api` ganha só um **comentário** apontando a mesma lacuna (sem
consumidor hoje, então sem urgência) — para não ser esquecida se `apps/api`
algum dia passar a consumir `ListTasksAsync`. É a única linha tocada em
`apps/api` nesta mudança, e não altera nenhum comportamento.

**Alternativa considerada**: deixar sem o filtro, confiando que `contextId`
(gerado pelo SDK A2A) nunca colide entre agentes na prática. Rejeitada — o
filtro é uma linha de código e elimina uma classe de bug totalmente
evitável, mesmo que de baixa probabilidade.

### 3. Reconstrução da sessão só usa a última task `completed`, não uma lista

Como o `AgentSession` serializado já é cumulativo (contém a sessão inteira até
aquele ponto, reduzida — Decisão 4), basta
`ListTasksAsync(new ListTasksRequest { ContextId = contextId, Status =
TaskState.Completed, PageSize = 1 })` (ordenado por `StatusTimestamp desc`,
comportamento já existente) para obter o ponto de partida — não é necessário
paginar por várias tasks nem reconstruir nada manualmente.

### 4. Limite de histórico via `IChatReducer` próprio (não o `MessageCountingChatReducer` nativo — esse é experimental), plugado no `InMemoryChatHistoryProvider`

`ChatClientAgent` passa a ser construído com `ChatClientAgentOptions {
ChatHistoryProvider = new InMemoryChatHistoryProvider(new
InMemoryChatHistoryProviderOptions { ChatReducer = new
RecentMessageChatReducer(MaxHistoryMessages) }) }` (constante global em
`AgentExecutionService`, não configurável por agente — non-goal explícito).
Confirmado via decompilação (`InMemoryChatHistoryProvider.ProvideChatHistoryAsync`)
que, com o `ChatReducerTriggerEvent` padrão (`BeforeMessagesRetrieval`), a
redução **reatribui `state.Messages`** — ou seja, muta o estado que depois é
serializado de volta para `Metadata`, não é só um recorte transitório para a
chamada ao LLM. Isso significa que o limite também contém o tamanho do que
fica persistido no Postgres, não só o custo/latência da chamada.

**Achado durante a implementação, não previsto na proposta original desta
Decisão**: o `MessageCountingChatReducer` nativo do
`Microsoft.Extensions.AI` — cogitado inicialmente para este propósito — é
marcado `[Experimental("MEAI001")]` (junto com `SummarizingChatReducer` e
`ReducingChatClient`; a família de reducers concretos inteira é experimental
nessa versão do pacote). Referenciá-lo exige suprimir o diagnóstico e aceitar
que o tipo pode mudar ou ser removido em uma atualização futura sem aviso de
breaking change — o mesmo tipo de risco que o design.md de
`backend-agente-a2a-mvp` (Decisão 2) já registrou como motivo para evitar
`Microsoft.Agents.AI.Hosting.A2A.AspNetCore` quando havia alternativa.
Confirmado (decompilando `Microsoft.Extensions.AI.Abstractions`) que
**`IChatReducer` (a interface) e `InMemoryChatHistoryProviderOptions.ChatReducer`
(a propriedade que a consome) não são experimentais** — só as implementações
concretas prontas são. Decisão: implementar `RecentMessageChatReducer`
(`apps/workers`, próprio) contra a interface estável `IChatReducer`,
replicando a mesma semântica do `MessageCountingChatReducer` (mantém a
primeira mensagem de sistema, se houver, e as `N` mensagens não-system mais
recentes) sem depender do tipo experimental — ver Decisão 5 sobre por que,
na prática, a cláusula "preserva mensagem de sistema" nunca chega a se
aplicar neste código de qualquer forma. Simplificação deliberada em relação
ao original: não exclui mensagens de function call/result do recorte, porque
nenhum agente usa tools/MCP ainda (non-goal explícito de
`backend-agente-a2a-mvp`) — não há esse tipo de conteúdo para excluir hoje.

**Alternativas consideradas**:
- **Usar `MessageCountingChatReducer` nativo, suprimindo `MEAI001`**. Evita
  reimplementar uma lógica que o framework já tem pronta, mas amarra o
  comportamento a uma API que a própria Microsoft documenta como sujeita a
  mudar ou desaparecer sem aviso — para uma lógica pequena o suficiente para
  reimplementar com poucas linhas contra uma interface estável, o risco não
  se paga. Rejeitada.
- **Orçamento por tokens em vez de contagem de mensagens**. Mais preciso
  para estourar a janela de contexto do modelo, mas exigiria estimar tokens
  por provider (cada um tokeniza diferente) e nenhum requisito de produto
  pede esse nível de precisão ainda. Rejeitada por ora — `RecentMessageChatReducer`
  resolve o requisito mínimo ("não crescer sem limite") sem essa
  complexidade adicional.

### 5. `Instructions` (system prompt) nunca fica preso dentro da sessão persistida — sempre vem fresco do banco a cada execução

Dúvida investigada (decompilando `ChatClientAgent` real, sem assumir): editar
`Instructions` de um agente via `PUT /agents/{id}` no meio de uma conversa em
andamento poderia deixar a sessão desserializada "presa" a um system prompt
antigo? **Confirmado que não**, por construção:

- `ChatClientAgent.Instructions` é só um getter para
  `_agentOptions.ChatOptions.Instructions` — e o construtor de conveniência
  usado por `AgentExecutionService` (`new ChatClientAgent(chatClient,
  agent.Instructions, agent.Name)`) guarda `Instructions` dentro de um
  `ChatOptions { Instructions = instructions }`, **nunca** como uma
  `ChatMessage` de `Role.System` inserida na lista de mensagens.
- `PrepareSessionAndMessagesAsync`/`CreateConfiguredChatOptions` (chamados a
  cada `RunAsync`) recalculam `chatOptions.Instructions` a partir do
  `Instructions` do `ChatClientAgent` **atual** — a instância construída
  nessa execução — e passam isso para `chatClient.GetResponseAsync(...)` como
  um parâmetro à parte da lista de mensagens, nunca gravado de volta em
  `state.Messages`/`AgentSession.StateBag` (`NotifyProvidersOfNewMessagesAsync`
  só persiste `inputMessagesForChatClient` + a resposta do modelo — nunca
  `chatOptions.Instructions`).
- `AgentExecutionService` já lê `agent.Instructions` fresco do banco
  (`dbContext.Agents...FirstOrDefaultAsync`) e constrói um `ChatClientAgent`
  novo em **toda** execução (código existente, não modificado por esta
  mudança).

Ou seja: `Instructions` nunca entra na sessão serializada por este caminho de
código — a cláusula "preserva a primeira mensagem de sistema" do
`MessageCountingChatReducer` (Decisão 4) é comportamento genérico do reducer,
mas nunca chega a se aplicar aqui, porque nenhuma `ChatMessage` de sistema é
inserida em `state.Messages` por este fluxo. Resultado prático: uma edição de
`Instructions` via `PUT /agents/{id}` passa a valer a partir da **próxima**
mensagem de qualquer `contextId` desse agente, imediatamente, independente do
que estiver guardado na sessão desserializada — sem ambiguidade entre
"antigo vence" e "novo vence", porque o antigo nunca chega a ser gravado.

Como isso depende de comportamento de um pacote externo (não sob nosso
controle), fica coberto por teste de integração dedicado (task 7.4) em vez de
só documentado — mesmo padrão de "confirmação técnica" já usado no design.md
de `backend-agente-a2a-mvp` (Decisão 4 daquele documento).

### 6. Turnos `failed`/`rejected` não entram no histórico — de graça, pela ordem das operações

`task.Metadata["conversationSession"]` só é escrito no caminho de sucesso de
`AgentExecutionService.ExecuteAsync` (depois de `RunAsync` retornar e antes do
`SaveTaskAsync` que acompanha `AddArtifactAsync`+`CompleteAsync`). No `catch`
que leva a task a `failed`, nada é escrito — a última sessão persistida
continua sendo a de uma task `completed` anterior (ou nenhuma, se for a
primeira). Tasks `rejected` nunca chegam a `apps/workers` (rejeitadas em
`apps/api`/`EnqueueingAgentHandler` antes de publicar o job), então nem
entram nesse fluxo. Nenhum filtro adicional é necessário além de já buscar só
tasks com `Status = Completed` (Decisão 3).

### 7. Lock consultivo do Postgres (`pg_advisory_lock`) por `(agentId, contextId)`

`TaskJobConsumer` já roda com `prefetchCount: 1` por canal — dentro de **uma**
instância do worker, mensagens são processadas sequencialmente, sem corrida.
O risco real é **entre instâncias**: nada no `docker-compose.yml` ou no
código impede rodar `apps/workers` com mais de uma réplica no futuro (é o
próximo passo natural de escala para um consumidor de fila), e o RabbitMQ
distribui mensagens entre consumidores concorrentes sem nenhuma noção de
"mesmo `contextId` → mesma instância". Duas mensagens do mesmo `contextId`
caindo em instâncias diferentes ao mesmo tempo poderiam cada uma ler "sem
sessão anterior"/uma sessão desatualizada antes da outra escrever a sua —
_lost update_ silencioso e permanente de um turno da conversa.

Decisão: `AgentExecutionService.ExecuteAsync` adquire, logo no início, um
`pg_advisory_lock` de sessão (não de transação) com chave derivada de
`(agentId, contextId)`, e libera (`pg_advisory_unlock`, em `finally`/
`await using`) só ao final — cobrindo leitura da sessão anterior → `RunAsync`
→ escrita da nova. Como o método hoje abre múltiplas `IServiceScope`/
`AppDbContext` de vida curta (uma por operação de store), o lock precisa de
**uma conexão Postgres dedicada, aberta explicitamente para essa finalidade**
e mantida viva pela duração inteira do método — não pode ser obtido dentro de
um dos escopos curtos existentes, que fecham antes do método terminar. Se o
processo morrer sem liberar, o Postgres libera o advisory lock automaticamente
ao encerrar a conexão da sessão.

**Custo, dois ângulos distintos** (vale separar, porque um afeta só o caso
raro e o outro afeta toda mensagem):
- **Serialização entre mensagens do mesmo `contextId`**: só tem efeito
  observável quando duas mensagens do mesmo `contextId` realmente colidem no
  tempo — caso raro (turnos de uma conversa já são sequenciais do ponto de
  vista do usuário).
- **Conexão Postgres dedicada mantida aberta durante toda a chamada ao
  LLM**: isso se aplica a **toda** mensagem em processamento, não só às que
  colidem em `contextId` — cada execução de `ExecuteAsync` passa a segurar
  uma conexão extra (além das que os `IServiceScope`/`AppDbContext` de vida
  curta já abrem e fecham) pelo tempo inteiro da chamada ao LLM, que é a parte
  mais lenta do método por uma ordem de grandeza. Hoje, com o worker rodando
  como instância única e `prefetchCount: 1`, isso é uma conexão a mais por
  vez — irrelevante. Se `apps/workers` escalar para múltiplas réplicas
  processando em paralelo, esse custo cresce linearmente com o número de
  mensagens simultâneas em voo, e passa a competir por espaço no
  `max_connections` do Postgres junto com as demais conexões de `apps/api` e
  das outras réplicas do worker — relevante o suficiente para monitorar
  quando esse cenário deixar de ser hipotético (ver Open Questions, lock
  distribuído via Redis/Valkey).

**Alternativas consideradas**:
- **Lock de transação (`pg_advisory_xact_lock`)** dentro de cada operação de
  store isolada. Rejeitada — só cobriria uma operação por vez (ex. só o
  `SaveTaskAsync` final), não a seção crítica inteira (leitura → `RunAsync` →
  escrita), que é onde a corrida de fato acontece.
- **Exchange de hash consistente no RabbitMQ**, roteando sempre o mesmo
  `contextId` para a mesma instância/fila. Resolveria na raiz (nunca haveria
  duas instâncias competindo pelo mesmo `contextId`), mas exigiria mudar como
  `apps/api`/`EnqueueingAgentHandler` publica a mensagem (routing key por
  `contextId`) — fere o non-goal explícito de não mudar `apps/api`. Rejeitada.
- **Documentar como risco aceito** (mesmo padrão usado para o
  `AgentA2AServerRegistry` ser cache em memória por processo, no design.md de
  `backend-agente-a2a-mvp`). Rejeitada aqui: naquele caso a consequência era
  duplicação de objetos em memória, sem corrupção de dado; aqui a consequência
  é perda silenciosa e permanente de um turno de conversa — grave o
  suficiente para não apenas documentar, já que a mitigação (advisory lock)
  é barata e não exige mudança de schema nem de `apps/api`.

### 8. Estender `tests/CrossAppTaskStoreCompatibility.Tests` em vez de criar verificação paralela

Como o `Metadata["conversationSession"]` é um campo novo dentro do mesmo
`payload` jsonb já coberto pela Decisão 8 do design.md de
`backend-agente-a2a-mvp`, o risco de divergência de schema entre as duas
implementações de `PostgresTaskStore` se aplica igual aqui. Decisão: adicionar
um cenário a `PostgresTaskStoreCompatibilityTests` — uma task `completed` com
`Metadata` populado, escrita por um `PostgresTaskStore`, lida corretamente
(campo presente e com o mesmo conteúdo) pelo outro — em vez de um mecanismo de
verificação novo e paralelo.

### 9. Retry curto em `GetTaskAsync` no início de `ExecuteAsync` — corrige uma corrida pré-existente entre `apps/api` e o RabbitMQ, achada via teste manual desta change

**Achado durante teste manual desta change** (não é um problema de histórico de
conversa, mas bloqueava a verificação manual dela — corrigido aqui por decisão
explícita do usuário, fora do escopo original da proposta): ao enviar uma
segunda mensagem no mesmo `contextId`, o worker falhava com "Task ... não
encontrada no store", e uma consulta posterior mostrava a task presa em
`TASK_STATE_SUBMITTED` para sempre.

Causa raiz, confirmada lendo `A2AServer.SendMessageAsync` (decompilado, pacote
`A2A` 1.0.0-preview2): o handler (`EnqueueingAgentHandler.ExecuteAsync`) roda
em `Task.Run` **concorrente** ao consumo do `AgentEventQueue`
(`MaterializeResponseAsync`), que é quem de fato persiste cada evento via
`ApplyEventAsync`/`SaveTaskAsync`. `TaskUpdater.SubmitAsync()` só escreve o
evento "submitted" no channel em memória (retorna quase instantaneamente, sem
esperar o consumidor) — o `SaveTaskAsync` real (o `INSERT` que cria a linha em
`a2a_tasks`) só acontece do lado do consumidor, em paralelo ao handler, que
segue seu próprio caminho: uma leitura de estado do agente no banco, e então
`taskJobPublisher.PublishAsync(...)` no RabbitMQ — a última coisa que o
handler faz antes de retornar. Nada ordena essas duas cadeias uma em relação
à outra: se `[leitura de estado do agente + publish no RabbitMQ]` (produtor)
terminar antes do `INSERT` (consumidor) commitar, um worker rápido pode
consumir a mensagem e chamar `GetTaskAsync` antes da linha existir. Esse
`GetTaskAsync` retornando `null` já registra um erro e retorna **sem lançar
exceção** — `TaskJobConsumer` então dá `BasicAckAsync` normalmente, a
mensagem é descartada sem nenhuma nova tentativa, e a task fica presa em
`submitted` para sempre.

Essa corrida é anterior a esta change (existe desde `backend-agente-a2a-mvp`)
e não tem nenhuma relação com histórico de conversa — só nunca tinha sido
exercitada de ponta a ponta antes porque ninguém havia testado manualmente um
segundo `SendMessage` no mesmo `contextId` contra o worker real até agora.

Decisão: `AgentExecutionService.ExecuteAsync` tenta `GetTaskAsync` até 5 vezes,
com 100ms entre tentativas (~400ms de espera adicional no pior caso — 
irrelevante perto da latência de uma chamada ao LLM), antes de desistir e
logar o erro (mesmo comportamento de hoje se as tentativas se esgotarem).
Fica inteiramente em `apps/workers` — não precisa de nenhuma mudança em
`apps/api`, então o non-goal original ("nenhuma mudança em apps/api") não é
violado por este fix adicional.

**Atualização, achada testando o próprio fix (terceira mensagem de um
contextId novo)**: `EnqueueingAgentHandler.ExecuteAsync` grava a task em
**dois** eventos separados, não um — `updater.SubmitAsync()` cria a linha
(`State=Submitted`, `History` nulo) e só depois, após a leitura de estado do
agente (`GetAgentStateAsync`), `eventQueue.EnqueueMessageAsync(context.Message)`
grava a mensagem do usuário nela (via um `GetTaskAsync`/`SaveTaskAsync`
independente dentro de `ApplyEventAsync`) — enquanto, concorrentemente, o
handler publica no RabbitMQ. O retry original só checava "a task existe", o
que a torna **mais** propensa a pegar exatamente a janela entre esses dois
eventos (task já existe, mas ainda sem a mensagem do usuário) do que o código
sem retry — sem o retry, essa janela exigia um timing improvável para ser
alcançada; com o retry, o worker fica tentando exatamente até ela aparecer, e
frequentemente pega o meio do caminho em vez do fim. Sintoma: não mais "task
não encontrada", e sim `RunAsync` lançando `ArgumentException: Argument is
whitespace (Parameter 'message')`, porque `ExtractLatestUserText` retorna
string vazia para uma task sem `History`.

Decisão revisada: `GetTaskWithRetryAsync` passa a exigir task encontrada **e**
com uma mensagem de usuário utilizável (`ExtractLatestUserText` não vazio) —
não só task encontrada — antes de considerar a tentativa bem-sucedida. Mesmo
orçamento de tentativas/atraso (5×100ms), já generoso o suficiente para cobrir
as duas escritas sequenciais do lado do `apps/api`.

**Alternativas consideradas**:
- **Ordenar o lado de `apps/api`** (ex. aguardar o `SaveTaskAsync` do evento
  "submitted" antes de publicar no RabbitMQ). Corrigiria na raiz, mas exigiria
  mudar `EnqueueingAgentHandler`/como o `A2AServer` é composto em `apps/api`
  — non-goal explícito desta change, e um escopo bem maior que o necessário
  para o sintoma observado.
- **NACK com requeue no RabbitMQ quando a task não é encontrada**, em vez de
  (ou além de) retry local. Rejeitada por ora: exigiria decidir uma política
  de requeue/backoff/dead-letter nova (`TaskJobConsumer` hoje nunca faz
  requeue, nem em exceções) — mudança de comportamento maior que o necessário
  para uma corrida da ordem de milissegundos; o retry local já resolve o
  sintoma real sem mexer na semântica de entrega da fila.
- **Job de reconciliação** que encontra tasks presas em `submitted` além de um
  tempo limite e as reprocessa. Resolveria também outras causas de task presa
  (não só esta corrida específica), mas é infraestrutura nova e maior que o
  necessário para este achado pontual — fica como possível trabalho futuro se
  tasks presas em `submitted` continuarem aparecendo por outros motivos.

### 10. Sessão serializada guardada como string JSON escapada em `Metadata` — não como `JsonElement` aninhado

**Segundo achado via teste manual desta change**, também corrigido aqui por
decisão explícita do usuário: a segunda mensagem de qualquer conversa passou
a falhar com `System.Text.Json.JsonException: The metadata property is
either not supported by the type or is not the first property...`, path
`$.messages[0].contents[0].$type`.

Causa raiz: `Microsoft.Extensions.AI.AIContent` (o tipo usado dentro de
`ChatMessage.Contents`, parte do `AgentSession` serializado — Decisão 1) é
anotado `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]`
(confirmado decompilando `Microsoft.Extensions.AI.Abstractions` 10.6.0) — o
`System.Text.Json` só consegue reconstruir o tipo concreto (`TextContent`,
`FunctionCallContent`, etc.) se `"$type"` for a **primeira** propriedade do
objeto JSON. `jsonb` do Postgres **não preserva ordem de propriedades de
objetos** — é um comportamento documentado do tipo (armazenamento binário
decomposto, normalizado; ao contrário do tipo `json`, que guarda o texto
original). Guardar o `JsonElement` da sessão serializada diretamente dentro
de `Metadata["conversationSession"]` (como a Decisão 1 originalmente
descrevia) significa que, assim que essa coluna for lida de volta do
Postgres, a ordem das propriedades dentro de cada `AIContent` pode já não
ter `"$type"` primeiro — quebrando a desserialização na task seguinte.
Confirmado que `A2A.Part` (usado em `History`/`Artifacts`, funcionando desde
sempre) **não** tem esse problema: é uma classe não-polimórfica, "usa
presença de campo para indicar o tipo de conteúdo" (comentário do próprio
tipo) — não depende de nenhuma ordem de propriedade. O problema é específico
do tipo introduzido por esta change (`AIContent`), não do mecanismo
`Metadata` em si.

Decisão: em vez de guardar o `JsonElement` da sessão serializada diretamente,
reencodá-lo como uma **string JSON escapada** (`ConversationSessionCodec`,
novo, `apps/workers`) antes de atribuir a `Metadata["conversationSession"]` —
`JsonSerializer.SerializeToElement(serializedSession.GetRawText())`. Uma
string é um valor escalar: o `jsonb` não tem como "olhar dentro" dela e
reordenar nada — o conteúdo é preservado byte a byte, exatamente como
qualquer outro valor de texto. Ao ler de volta, decodifica
(`JsonDocument.Parse(valor.GetString())`) antes de repassar para
`DeserializeSessionAsync`. Não muda a Decisão 1 (ainda é `Metadata`, ainda é
o mesmo `payload` jsonb, nenhuma tabela/coluna nova) — só corrige como o
valor é encodado dentro dela.

Verificado com um teste unitário dedicado
(`ConversationSessionCodecTests`, sem precisar de Postgres real): serializa
uma sessão de verdade com conteúdo polimórfico, simula a reordenação que o
jsonb faz (reconstrói a árvore JSON com as propriedades de cada objeto
invertidas) e confirma que (a) sem o codec, a desserialização de fato quebra
— reproduzindo o bug relatado — e (b) com o codec, sobrevive à mesma
reordenação e desserializa corretamente. Esse teste roda sem Docker, então
pôde ser executado e confirmado durante esta sessão (ao contrário dos testes
de integração com Testcontainers, que continuam exigindo verificação manual
— ver Risks/Trade-offs).

**Alternativas consideradas**:
- **Mudar a coluna `payload` de `jsonb` para `json`** (que preserva o texto
  original, incluindo ordem de propriedades). Resolveria na raiz, mas
  `payload` é a mesma coluna compartilhada com `apps/api`
  (`backend-agente-a2a-mvp`, Decisão 4) — mudar seu tipo é uma migration em
  uma tabela que só `apps/api` provisiona, reabrindo exatamente o problema de
  propriedade de migration que a Decisão 1 já evitou. Rejeitada.
- **Configurar `JsonSerializerOptions` para não usar polimorfismo baseado em
  metadata** (ex. um `IContractResolver`/converter próprio). Rejeitada:
  `SerializeSessionAsync`/`DeserializeSessionAsync` são do framework — não
  temos acesso para injetar opções de serialização diferentes das que ele já
  usa internamente.
- **Guardar a sessão em uma tabela separada como `text`/`json`** (não
  `jsonb`). Resolveria o problema de ordenação, mas reabre a mesma questão de
  propriedade de migration que a Decisão 1 rejeitou (tabela nova exclusiva de
  `apps/workers`). Rejeitada pelo mesmo motivo.

## Risks / Trade-offs

- **[Risco] Tasks `completed` anteriores a este deploy não têm
  `Metadata["conversationSession"]`** → tratadas como "sem sessão anterior"
  (comportamento idêntico ao atual, sem regressão); a primeira mensagem de uma
  conversa em andamento no momento do deploy não recupera o histórico anterior
  ao deploy, mas conversas inteiras a partir dele funcionam normalmente.
- **[Risco] Dado potencialmente sensível dentro do blob serializado** — a
  própria doc do framework alerta que sessões serializadas podem conter
  conteúdo de conversa e PII. Mitigação: mesmo nível de exposição que
  `History`/`Artifacts` já têm hoje dentro do mesmo `payload` jsonb — nenhuma
  superfície de risco nova introduzida.
- **[Risco] Lock consultivo retido além do necessário por bug de liberação**
  → mitigação: `finally`/`await using` garante liberação mesmo em exceção;
  Postgres libera locks de sessão automaticamente se a conexão cair.
- **[Trade-off] `MessageCountingChatReducer` corta por contagem de mensagens,
  não por tokens** — conversas com mensagens muito longas podem ainda assim
  se aproximar da janela de contexto do modelo mesmo dentro do limite de `N`
  mensagens. Aceitável para esta fatia (ver Open Questions).
- **[Trade-off] Advisory lock serializa (não paraleliza) mensagens do mesmo
  `contextId` entre instâncias** — aceitável: turnos de uma mesma conversa já
  são inerentemente sequenciais do ponto de vista do usuário (que normalmente
  espera a resposta antes de mandar a próxima mensagem); o custo é uma espera
  ocasional, não um erro.
- **[Trade-off] Advisory lock mantém uma conexão Postgres dedicada aberta
  durante toda a chamada ao LLM, para toda mensagem em processamento** —
  distinto do trade-off acima (esse aqui se aplica mesmo sem nenhuma colisão
  de `contextId`). Irrelevante com o worker em instância única; cresce
  linearmente se `apps/workers` escalar para múltiplas réplicas
  processando em paralelo (ver Decisão 7 e Open Questions).
- **[Risco residual] O retry da Decisão 9 reduz drasticamente as duas janelas
  da corrida (task inexistente, e task existente sem mensagem do usuário —
  ambas na ordem de milissegundos), mas não as elimina por completo** — se o
  Postgres estiver anormalmente lento (ex. sob carga pesada) ou as duas
  escritas de `apps/api` demorarem mais que o orçamento de retry, a task
  ainda pode ficar presa em `submitted`. Mitigação aceita para esta fatia:
  mesmo comportamento de hoje nesse caso extremo (log de erro, sem crash do
  worker), só menos provável de acontecer; um job de reconciliação (ver Decisão 9,
  alternativas) fica como possível evolução se isso continuar aparecendo.

## Migration Plan

Sem migration de banco — `Metadata` já faz parte do tipo `AgentTask` do
pacote `A2A` e da coluna `payload` jsonb existente. Deploy normal de
`apps/workers`; nenhuma coordenação especial com `apps/api` (que nunca lê nem
escreve esse campo — só ganha um comentário, ver Decisão 2). Rollback:
reverter o deploy de `apps/workers` — tasks gravadas com `Metadata` populado
pela versão nova continuam lidas normalmente pela versão anterior do código
(que simplesmente ignora o campo extra), sem nenhuma migration para desfazer.

## Open Questions

- Valor exato de `MaxHistoryMessages` (proposto: 20 mensagens não-system como
  ponto de partida) — ajustar depois de observar custo/latência real; é uma
  constante isolada, fácil de mudar sem redesenho.
- Se conversas longas exigirem preservar mais contexto do que truncamento
  simples permite, resumir em vez de descartar (o padrão do
  `SummarizingChatReducer` nativo do `Microsoft.Extensions.AI`) é uma
  evolução possível de `RecentMessageChatReducer` — mas esse tipo nativo
  também é `[Experimental("MEAI001")]` (ver Decisão 4), então adotá-lo
  exigiria a mesma análise de risco feita ali (reimplementar contra
  `IChatReducer`, ou suprimir o diagnóstico e aceitar o risco). Fica
  registrado como evolução futura, fora do escopo desta fatia.
- **Lock distribuído (Redis/Valkey) como evolução do mecanismo da Decisão 7**
  — não da persistência da sessão em si, que continua no Postgres/`Metadata`
  (mover o dado da sessão para Redis reabriria a Decisão 1 com um trade-off
  pior: perderia a atomicidade entre gravar a task e gravar a sessão, sem
  reduzir o custo real, que é causado pelo lock ficar retido, não pelo local
  de armazenamento da sessão). Gatilho para revisitar: `apps/workers`
  escalar para múltiplas réplicas de verdade — cenário que não existe hoje.
  Trade-off principal: uma conexão Redis multiplexada/compartilhada em vez de
  uma conexão Postgres dedicada presa por toda a duração da chamada ao LLM
  (ver Decisão 7), com o benefício adicional de um lock em Redis ser
  inerentemente efêmero — perdê-lo num restart do Redis não é grave, já que a
  mensagem em voo naquele momento exigiria reprocessamento de qualquer forma.
  Fora do escopo desta fatia porque o gatilho (múltiplas réplicas reais) não
  existe ainda.
