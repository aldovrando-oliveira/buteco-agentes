## 1. Guardas primeiro, vermelhos contra o defeito (`apps/workers`)

A convenção 15 é a ordem, não um passo final: o guarda vale depois de ter
reprovado contra o defeito real. A exploração registrou que essa rodada costuma
achar defeito no próprio teste — se o guarda passar em `HEAD`, ele está errado,
e o conserto é dele, não da produção.

- [x] 1.1 (`apps/workers`) Criar `tests/Buteco.Workers.Tests/ConversationContextLockFailureTests.cs` no molde de `AgentDelegationConcurrencyTests` — `[Collection(WorkerHostCollection.Name)]` + `IClassFixture<WorkerInfrastructureFixture>`, container próprio por classe, cada instância com seu próprio container de DI montado do zero.
- [x] 1.2 (`apps/workers`) Guarda "aquisição que estoura termina a task em `failed`": segurar o `pg_advisory_lock` de `(agentId, contextId)` por fora da aplicação, publicar um job para essa task, e afirmar que a task alcança `Failed` — nunca fica em `Working`. O host de teste encurta a espera por `Command Timeout=N` na connection string (D4/M6), nunca por knob de produção.
- [x] 1.3 (`apps/workers`) Guarda "duas instâncias, duas tasks do mesmo `(agentId, contextId)`, a primeira lenta": afirmar que **as duas** alcançam estado terminal e que a segunda não fica em `Working`. Fazer a primeira lenta pelo `IChatClient` mockado, não por comando de banco — o `Command Timeout` curto do host vale para todo comando EF.
- [x] 1.4 (`apps/workers`) Guarda "task `completed` não é sobrescrita quando a liberação do lock falha" — prende a alternativa recusada em D1, e sem ele nada impede alguém refazer a correção movendo o `await using` para dentro do `try`. **Montagem em D8, e a sincronização é uma chamada, nunca um relógio:** de dentro do `IChatClient` mockado (invocado no `try`, antes do `ApplyStepAsync(Complete)`), derrubar o backend dono do lock localizando-o pelo próprio par de chaves em `pg_locks` — `objsubid = 2` (a forma de duas chaves), `classid = hashtext(agentId)`, `objid = hashtext(contextId)`, os dois como inteiros sem sinal. O `objsubid` não muda o resultado hoje; ele impede o guarda derrubar o backend errado se a forma de chave única de 64 bits aparecer depois (D8). Asserção: a task fica `Completed` com o artefato do agente preservado. Proibido `Task.Delay`/`Thread.Sleep` para posicionar a derrubada.
- [x] 1.5 (`apps/workers`) Guarda "a falha de aquisição não pendura escopo/conexão", **por esgotamento de pool com o teto baixado** (D9): host próprio com `Maximum Pool Size=3`, lock segurado por fora, 5 jobs publicados. Asserção: as 5 tasks alcançam `Failed`. Sem o descarte de D3 o pool esgota e o worker não consegue nem gravar as próprias falhas — as tasks ficam em `Working`, que é o defeito original por outra porta. Não contar `pg_stat_activity`: medido (M9) que não discrimina sozinho. Não elevar o número de iterações acima do teto default de 100 — é a forma recusada, ~5 min de suíte contra ~10 s desta.
- [x] 1.6 **Rodar 1.2–1.5 contra `HEAD` e registrar o resultado de cada um, com a causa atribuída.** 1.2, 1.3 e 1.5 devem reprovar; 1.4 deve **passar** em `HEAD` (o defeito que ele previne ainda não existe — ele é guarda de regressão contra a correção errada). **O vermelho de 1.5 aqui é atribuível a D1, não a D3** — sem tratamento da aquisição a exceção escapa e as tasks ficam em `working` antes de o pool chegar a esgotar; a atribuição a D3 só vem do passo intermediário da task 2.2. Anotar no `design.md` qualquer guarda cujo resultado divirja disso, com a causa real (convenção 9).

## 2. Correção, uma de cada vez (`apps/workers`)

Esta change corrige **duas** coisas independentes — D1 (tratamento próprio da
aquisição) e D3 (descarte do escopo) —, e a ordem aqui não é arbitrária.

O guarda 1.5 reprova em `HEAD`, mas **pelo motivo de D1, não pelo de D3**: sem
tratamento da aquisição, as tasks ficam em `working` porque a exceção escapou, e
o vazamento de escopo nunca chega a ser o que faz o guarda reprovar. O único
estado que isola D3 é o **intermediário** — D1 aplicada, D3 não. Aplicar as duas
juntas faria D3 entrar sem nunca ter tido um guarda vermelho contra o seu
próprio defeito, que é a condição que a convenção 15 existe para evitar.

- [x] 2.1 (`apps/workers`) **D1 primeiro.** `AgentExecutionService.ExecuteAsync`: dar `try/catch` próprio à aquisição do lock (`:158`), que loga, termina a task como `failed` via `ApplyStepAsync`, dispara a push notification pelo caminho existente e retorna. **Manter o `await using` e o bloco `try/catch` existente exatamente onde estão** — o motivo de não os fundir está em D1 e é o que 1.4 prende.
- [x] 2.2 **Passo intermediário, com D1 aplicada e D3 ainda não — rodar 1.2, 1.3 e 1.4: verdes; e 1.5: ainda VERMELHO.** É aqui que o vermelho de 1.5 passa a ser atribuível a D3: agora a aquisição é tratada, mas o escopo vaza, o pool capado em 3 esgota e `ApplyStepAsync(Fail)` não consegue conexão — as tasks voltam a `working` **pelo outro motivo**. M8 já mediu esse par de formas lado a lado; o que este passo faz é a rodada de guardas exercitá-lo. **Registrar o resultado, e registrar a atribuição** — se 1.5 ficar verde aqui, ou o guarda não exercita o vazamento ou D3 já não é um defeito, e as duas saídas exigem parar e apurar antes de seguir.
- [x] 2.3 (`apps/workers`) **D3 depois.** `ConversationContextLock.AcquireAsync`: envolver a abertura da conexão e o `pg_advisory_lock` em `try/catch` que descarta o `IServiceScope` e **relança a exceção original sem alterá-la** — D1 depende do tipo e da mensagem dela para o log.
- [x] 2.4 (`apps/workers`) Conferir que o log da falha de aquisição nomeia `AgentId`, `ContextId` e `TaskId`. Sem `ContextId` não há como ligar a falha à conversa que segurava o lock, que é a única informação acionável desta falha. Isto é o log de erro do caminho terminal novo, não instrumentação de delegação (non-goal).
- [x] 2.5 **Rodar 1.2–1.5 de novo, com as duas correções aplicadas: os quatro verdes.** Se 1.4 virou vermelho, a correção alargou o alcance do `catch` — é o defeito que D1 recusa. Se 1.5 continuou vermelho, D3 não cobriu o caminho que o guarda exercita.

## 3. Registros de mecanismo (`apps/workers`)

Os dois vão no arquivo, não no `02` — é lá que quem investiga o sintoma abre
primeiro (D6).

- [x] 3.1 (`apps/workers`) Em `ConversationContextLock`: registrar que **o Npgsql não libera advisory lock ao devolver a conexão ao pool** (medido: `[4] depois de Close(), o lock ainda existe? True`), e que por isso o desenho depende do `pg_advisory_unlock` explícito de `DisposeAsync` — fechar a conexão **não** basta.
- [x] 3.2 (`apps/workers`) Em `ConversationContextLock`: registrar que `hashtext` pode colidir, e que uma colisão serializa dois pares `(agente, contexto)` não relacionados — degradação silenciosa, nunca corrupção.
- [x] 3.3 (`apps/workers`) Em `ConversationContextLock`: registrar que a espera pelo lock é limitada pelo `CommandTimeout` do Npgsql (30 s por default, configurável pela connection string), que a correção de 2.1 **depende** desse limite existir, e que `Command Timeout=0` reintroduz o defeito em outra forma — task presa em `working` porque a aquisição nunca retorna (D2).

## 4. Verificação (`apps/workers`)

- [x] 4.1 (`apps/workers`) Suíte `Buteco.Workers.Tests` inteira, verde, com o número **antes** e **depois** lado a lado — nunca só "passou" (convenção 19). Testcontainers exige `DOCKER_HOST` do Podman **e** `TESTCONTAINERS_RYUK_DISABLED=true` nesta máquina.
- [x] 4.2 (`apps/api`, `apps/inbox`) Rodar as duas suítes e comparar contra a baseline número a número. Não há mudança nesses apps, então qualquer divergência é pré-existente ou ambiental — e "pré-existente" só vale com a baseline na mão (convenção 19). `AgentDeactivationTests` de `apps/api` já reprova em classe e passa isolada: comparar contra esse baseline, não contra zero.
- [x] 4.3 (`tests/InboxOrchestratorRoundTrip.Tests`) Rodar — é o único teste que atravessa os três apps e o que afirmaria uma regressão no estado terminal vista do lado do `apps/inbox`.
- [x] 4.4 Conferir que nenhum arquivo **de produção** fora de `apps/workers/src/Buteco.Workers/Agents/` e `apps/workers/tests/` foi tocado. Três caminhos são exceção esperada e **não** contam como violação: os artefatos da própria change em `openspec/changes/lock-de-contexto-falha-terminal/`, o `02-HISTORICO_E_STATUS.md` (tasks 5.2 e 5.4) e — se o aviso da 5.3 for feito por escrito — a nota de operação para quem roda o piloto. Qualquer outro arquivo fora daqueles dois caminhos é violação: compose, `docs/deployment.md`, `docs/development.md`, `01-ARQUITETURA_E_CONVENCOES.md`, migrations e os outros apps são non-goal explícito.

## 5. Fechamento

- [x] 5.1 Projetar tamanho **agora**, não antes: criados e modificados separados, em pares com o teste (convenção 18). Régua desta superfície, recontada e **com o escopo colado no número** (D4): 13 sítios instanciam `AgentExecutionService` — 12 em `apps/workers/tests/` mais 1 em `tests/InboxOrchestratorRoundTrip.Tests/` — e há 13 definições privadas de `BuildHost` **em `apps/workers/tests/`**; são conjuntos diferentes que coincidem por acaso. O desenho escolhido custa zero de ambos. Citar sempre com o escopo: a mesma régua já circulou como 15 e como 16, cada uma somando um escopo diferente.
- [x] 5.2 Registrar no `02-HISTORICO_E_STATUS.md`: o defeito, o mecanismo medido, e que ele foi **aberto por subir para duas instâncias** — a troca que resolveu a delegação e abriu isto, que ninguém tinha escrito. Três coisas entram junto, e nenhuma delas é o mecanismo:
  - **A frequência esperada, e como ela vai ser lida errado.** Depois desta change, **toda** segunda mensagem que espere mais de 30 s pelo lock vira falha visível. Com uma delegação consumindo até 120 s, não é caso de borda: é o caso normal quando a conversa anterior delegou. Vão aparecer falhas logo depois do deploy, e a leitura natural de quem opera é "a change quebrou alguma coisa". Registrar nesta forma: **as falhas não são novas, a visibilidade é** — antes, a mesma conversa morria em silêncio com a task presa em `working`.
  - **A mudança no destino da mensagem do RabbitMQ.** Com a correção, `ExecuteAsync` retorna normalmente e a mensagem passa a ser **acked** em vez de nacked (`TaskJobConsumer.cs:55` em vez de `:60`). É o comportamento correto — a task alcançou estado terminal, não há o que reprocessar — e não estava escrito em lugar nenhum.
  - Que o limite de 30 s continua sendo o default do Npgsql e não uma escolha (D2), com o gatilho de quando ele vira decisão.
- [x] 5.3 **Avisar quem opera o piloto antes de subir**, não depois. Não é cerimônia: é o que impede um rollback motivado por um número que esta change existe para tornar visível. O aviso precisa dizer as duas coisas juntas — vão aparecer falhas de task, e elas já aconteciam em silêncio. Sem isso, o primeiro pico de `failed` parece regressão.
  - **Fechada sem destinatário, e o motivo está escrito.** Operador e
    desenvolvedor são a mesma pessoa nesta fase, então o aviso não tem a quem
    ser entregue. O propósito — impedir que alguém leia o primeiro pico de
    `failed` como regressão — ficou coberto pelo registro da 5.2 no
    `02-HISTORICO_E_STATUS.md`, que é o artefato que sobrevive a esta change e
    que vai ser lido quando a memória não servir. **Condição de reativação:** a
    task volta a existir no instante em que alguém entrar na operação sem ter
    acompanhado esta change.
- [x] 5.4 Registrar no `02-HISTORICO_E_STATUS.md`, como item em aberto com gatilho **e** posição: a varredura de `PendingDispatch` órfã em `Dispatching` (D5), e o ciclo `A→B→A` (change própria, a seguinte na fila). Gatilho sem posição é adiamento indefinido com outro nome.
- [x] 5.5 **Verificação pós-deploy, manual, e não é opcional:** as linhas **já** presas no piloto não são corrigidas por esta change (D5). Inspecionar `a2a_tasks` por tasks em `working` mais velhas que o máximo plausível de execução, e `pending_dispatches` por linhas em `Dispatching`, e registrar a contagem encontrada. É esse número que diz se o destravamento manual vira trabalho próprio.
  - **Medido em 20/09/2026 contra o Postgres do piloto, ANTES do deploy: zero
    dos dois.** Nenhuma `a2a_tasks` em `Working`/`Submitted`, nenhuma
    `pending_dispatches` em `Dispatching`. O destravamento manual **não vira
    trabalho próprio**.
  - **A fronteira, junto do número** (convenção 13): é observação pontual de
    **estado**, não de histórico — `a2a_tasks` não guarda transições, e linha
    destravada à mão não apareceria. **Não afirma que o defeito nunca ocorreu**,
    e **não é prova de que a correção funciona** (a medição é anterior ao
    deploy). A leitura mais provável, também não provada: volume concorrente
    insuficiente no piloto. Zero é consistente com "ainda não aconteceu", não
    com "não acontece".
  - **O que melhora:** com zero acumulado, o baseline pós-limpeza fica
    inequívoco — qualquer linha presa depois é posterior à correção.
