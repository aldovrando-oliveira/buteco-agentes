**Base:** `origin/main` em `1e250e6` (merge do PR #174, a #162), em 18/10/2026. Os
artefatos nasceram sobre `18a2f3b`; o rebase foi fast-forward (a branch não tinha
commits) e os nove arquivos da change saíram com o mesmo SHA-256. Entre as duas bases
entraram a #162 (serialização por sessão no inbox) e a #160/#168 (PRs #169/#172, MCP
por header customizado, que tocam `apps/workers` em `Mcp/` e `Infrastructure/`, fora
de `Messaging/`, `Agents/` e `Program.cs`). As referências de linha abaixo foram
reconferidas sobre `1e250e6`.

## 1. Baseline e conferências, antes de tocar código

- [x] 1.1 (`apps/workers`, `tests/InboxOrchestratorRoundTrip.Tests`) Baseline neste
      worktree sobre `1e250e6`, sem nenhuma edição (não reaproveitar número medido
      sobre `18a2f3b` nem o do `02` da #162, que é de `cb736db`/fechamento dela):
      `dotnet test` de `apps/workers/Workers.sln` e de
      `tests/InboxOrchestratorRoundTrip.Tests`, com o Podman de pé e `DOCKER_HOST`
      exportado. Guardar a lista de **nomes** de teste com estado (arquivo :: nome ::
      estado) no scratchpad da sessão. Falha na baseline é investigada antes de ser
      chamada de pré-existente (convenção 19).
      **Resultado, 18/10/2026, árvore sem edição de código** (só artefatos e o `02`):
      VM `podman-machine-default` (applehv, 6 CPUs, 6 GiB), **zero contêineres** em
      `podman ps` (o stack de dev e os `dotnet run` do checkout principal foram parados
      com autorização do dono), `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED`,
      `DOTNET_SYSTEM_NET_DISABLEIPV6` e **`TZ=America/Sao_Paulo`**.
      - `apps/workers`: **405/405**, 5m28s de parede (5m20s do vstest), load 3,97 →
        3,82, 42 classes.
      - `InboxOrchestratorRoundTrip`: **10/11**, 2m36s de parede (41 s do vstest), load
        3,82 → 5,67. Reprovou `OrphanDispatchRoundTripTests.Source3_…` na `:112`
        (`WaitForTaskIdAsync` da sentinela, `TimeoutException`). **Investigado:** a
        mesma árvore deu 11/11 e 11/11 em duas repetições seguidas (load 14,12 e 9,39
        na largada da primeira e da segunda). Intermitente, **1 em 3**, anterior à
        change; mecanismo mais provável na **#176** (convenção 23). Conta como
        baseline o estado de cada nome nas três rodadas; o fechamento compara contra
        `Source3` "passa às vezes".
      - **Duas tentativas descartadas, e por quê:** a primeira, com o stack de dev de
        pé, reprovou na subida de uma fixture (`proxy already running`, erro do
        Podman), contra o critério da `WorkerHostCollection` (zero contêineres); a
        segunda, sem `TZ`, deu round-trip 0/11 em 66 ms, com a checagem de fuso do
        `apps/api` reprovando (`'Brazil/East'`, `02` item f).
      - Listas por nome em
        `<scratchpad>/baseline/{workers,roundtrip}.names`, repetições em
        `<scratchpad>/baseline-repeticao-{1,2}.log`.
- [x] 1.2 (`apps/workers`) Conferir no `RabbitMQ.Client` 7.2.1 decompilado a
      assinatura exata do construtor de `CreateChannelOptions` e o tipo de
      `consumerDispatchConcurrency` (`ushort`), antes de escrever a chamada (convenção 6).
      **Resultado** (`ilspycmd` sobre `lib/net8.0/RabbitMQ.Client.dll`, versão de
      `Directory.Packages.props:51`): `CreateChannelOptions(bool
      publisherConfirmationsEnabled, bool publisherConfirmationTrackingEnabled,
      RateLimiter? outstandingPublisherConfirmationsRateLimiter = null, ushort?
      consumerDispatchConcurrency = 1)`. **Divergência pequena:** o tipo é `ushort?`,
      não `ushort`; a chamada passa `(ushort)N` e converte implicitamente. A pegadinha
      do achado 2 está confirmada no próprio construtor (default `1`, não `null`): só o
      construtor interno, usado quando `CreateChannelAsync` recebe `null`, herda o
      valor da `ConnectionConfig`. O `RateLimiter` omitido vira `null` (sem limitação),
      inócuo aqui porque as confirmações de publicação ficam desligadas.
- [x] 1.3 (`apps/workers`) Conferir por leitura de `AgentExecutionService` e das tools
      (delegação, conhecimento, histórico) quantas conexões Postgres uma execução
      segura **ao mesmo tempo** enquanto o `ConversationContextLock` está em posse. Se
      for mais de duas, reescrever o teto da D3 (`⌊(100 − 8) / k⌋`) antes do código e
      registrar a divergência (convenção 9). **Resultado: `k = 2`, a premissa da D3
      vale.** Uma conexão dedicada do lock (`ConversationContextLock.cs:112`,
      `OpenConnectionAsync` explícito, solta no `DisposeAsync`). Todo o resto é EF sem
      conexão aberta à mão, que pega e devolve conexão do pool por comando: o
      `AppDbContext` da execução (`AgentExecutionService.cs:95`), os escopos de
      `PostgresTaskStore`, da delegação (`AgentDelegationToolSetResolver.cs:97`, polling
      incluído), da busca de conhecimento (`KnowledgeToolSetResolver.cs:132`) e dos
      gravadores de métricas. Nada roda em paralelo dentro de uma execução: nenhum
      `Task.WhenAll`/`Task.Run`/`Parallel` em `apps/workers/src`, e as tool calls de um
      turno são **seriais**, porque o `ChatClientAgent` do `Microsoft.Agents.AI` 1.15.0
      monta o `FunctionInvokingChatClient` com o construtor padrão, e o default de
      `AllowConcurrentInvocation` é `false` (XML doc do `Microsoft.Extensions.AI`
      10.6.0). Logo, no máximo uma conexão de vida curta por vez. **Achado (convenção
      23): #175** — três comentários (`AgentExecutionService.cs:355`,
      `KnowledgeToolSetResolver.cs:128`, `ExecutionMetricsScope.cs:42`) afirmam que o
      `FunctionInvokingChatClient` pode invocar tools em paralelo; não pode, com o
      default. Ligar `AllowConcurrentInvocation` faria `k = 1 + tools no turno` e
      reabriria o teto; o gatilho está na issue.
- [x] 1.4 (`apps/workers`, `tests/InboxOrchestratorRoundTrip.Tests`) Conferir os 14
      harnesses que registram `TaskJobConsumer` e marcar, por harness, se depende de
      `N = 1` e por quê. Contagem refeita sobre `1e250e6` (`git grep
      "AddHostedService<TaskJobConsumer>"`, fora o `Program.cs`): **14 arquivos, 16
      pontos de registro**, os mesmos de `18a2f3b`; `TaskJobConsumerTests` tem três
      builders (`:1109`, `:1172`, `:1212`). Os cinco da D7 já estão listados; o
      `RoundTripFixture` ganhou com a #162 um terceiro chamador do mesmo
      `BuildWorkersHost` (`StartSecondWorkersAsync`, além da subida e do
      `RestartWorkersAsync`) e um segundo teste que depende de `N = 1`
      (`SessionSerializationRoundTripTests`, D7), sem harness novo. Os outros nove são
      `ChannelContextMessageTests`, `HistorySummarizationTests`,
      `Agents/AgentToolNamespaceTests`, `WorkerTests`,
      `Mcp/McpToolExecutionEndToEndTests` (registro agora em `:315`; a #169
      acrescentou `RoundTrip_CustomHeaderServer_…`, mais um teste de uma task por
      host, a conferir como os outros), `ConversationHistoryTests`,
      `TemporalContextMessageInstantTests`,
      `Knowledge/KnowledgeToolExecutionEndToEndTests` e
      `Notifications/PushNotificationEndToEndTests`. Resultado inline nesta tarefa.
      **Resultado: nenhum dos nove depende de `N = 1`.** Em todos, cada teste publica
      uma task e espera estado terminal (`PollUntilTerminalAsync`/`RunTurnAsync`) antes
      de publicar a próxima; conferido por varredura de cada corpo de teste (nenhum par
      de `PublishJobAsync` sem espera no meio) e por leitura dos casos de mais de uma
      task (`AgentToolNamespaceTests:302–323`, `ConversationHistoryTests:145–176` e os
      de `hostA`/`hostB`, que param um host antes de subir o outro). `WorkerTests` só
      sobe e para o host. Com no máximo uma task em voo por vez, `N = 4` não muda nada
      do que eles observam. Os nove herdam o default 4 sem registrar a opção (o
      `IOptions<TaskConsumerOptions>` sem `Configure` devolve a instância padrão). Cada
      classe tem containers próprios (`IClassFixture<WorkerInfrastructureFixture>`),
      então o host de uma classe nunca consome task de outra, estejam ou não na coleção
      `worker-hosts` (quatro dos nove não estão: `ChannelContextMessageTests`,
      `ConversationHistoryTests`, `TemporalContextMessageInstantTests`, `WorkerTests`).

## 2. Guardas vermelhos contra o código atual (testes)

**Desvio de ordem e de forma, registrado (convenção 9):**

- **A 3.1 (`TaskConsumerOptions`) foi feita ANTES dos guardas 1–4**, inerte (o
  consumidor ainda não a lê). Com ela os guardas compilam e ficam vermelhos **pelo
  comportamento**, e não só por não compilar; o código de produção que eles atacam
  (`TaskJobConsumer`) continuou intacto até a 3.4. Os harnesses ganharam o parâmetro
  `maxConcurrentExecutions` com default 1 já aqui, o que adianta a parte da 4.1 que
  toca `TaskJobConsumerTests` e `AgentDelegationConcurrencyTests`.
- **Guardas 1, 3 e 4 não são classe própria:** `Messaging/TaskJobConsumerConcurrencyTests.cs`
  é `partial` de `TaskJobConsumerTests` (que virou `partial`). Uma classe nova com host
  seria a 15ª da coleção `worker-hosts`, e a `WorkerHostCollection` manda recalibrar a
  referência de duração da suíte (6m37s com 14 classes) quando ela entra. Mesma fixture,
  mesmos helpers, arquivo separado como o design pede.
- Os vermelhos abaixo rodaram com zero contêineres de dev e as variáveis da 1.1, em
  18/10/2026 (~19:45–19:50, load 3,26 → ~5). Guardas filtrados, não a suíte.

- [x] 2.1 (`apps/workers`, testes) `Messaging/TaskConsumerConcurrencyValidationTests.cs`
      (guarda 5): `N = 0` e `N = 33` reprovam com mensagem que nomeia o valor e a
      faixa; `N = 1` e `N = 32` passam com a linha de sucesso. Vermelho por não
      compilar até a tarefa 3.2. **Vermelho visto:** 3 × `CS1061` ("`IHost` não contém
      uma definição para `ValidateTaskConsumerConcurrency`"). O arquivo ficou fora da
      árvore até a 3.2, para os guardas 1–4 compilarem. Acrescentados `N = -1` (fora) e
      o default (`4`, passa e registra a linha), que a spec "Valor padrão" pede.
- [x] 2.2 (`apps/workers`, testes) `Messaging/TaskJobConsumerConcurrencyTests.cs`,
      guarda 1: `N = 2`, três tasks lentas, exatamente duas em `Working` e
      `messages_ready == 1` (API de gerenciamento ou `QueueDeclarePassive`).
      XML doc diz que **este guarda é o que prende a pegadinha do construtor** de
      `CreateChannelOptions` (D1). Ver vermelho contra o código atual (uma só em
      `Working`). **Vermelho visto:** retrato `(working, submitted, prontas) = (1, 2, 2)`
      contra `(2, 1, 1)`. `messages_ready` lido por `QueueDeclarePassive` (o
      `message_count` do `declare-ok` conta só as prontas). O retrato é tirado depois de
      uma folga de 2 s, não no instante em que chega a duas em `Working`.
- [x] 2.3 (`apps/workers`, testes) Guarda 2 em `AgentDelegationConcurrencyTests`:
      delegação concluída numa instância só com `N = 2`. Ver vermelho contra o código
      atual (expira pelo timeout). **Vermelho visto:**
      `(Target invocado em 10 s, estado do Source, texto do Target na resposta)` =
      `(False, Completed, False)` contra `(True, Completed, True)`, em 36 s: a delegação
      expirou pelos 30 s e o Source completou com o resultado de falha.
- [x] 2.4 (`apps/workers`, testes) Guarda 3 em `TaskJobConsumerConcurrencyTests`:
      `N = 2`, duas execuções esperando o provedor, parada em menos de 10 s, dois jobs
      de volta à fila, zero advisory locks em `pg_locks`. **Vermelho visto:** retrato
      `(entraram, locks em voo, working, prontas, locks depois)` = `(1, 1, 1, 2, 0)`
      contra `(2, 2, 2, 2, 0)`; parada em 0,1 s. Os locks são lidos nas duas pontas
      (em voo e depois), como no guarda 2.5 da parada, para o `0` provar liberação.
- [x] 2.5 (`apps/workers`, testes) Guarda 4 em `TaskJobConsumerConcurrencyTests`:
      `N = 2` no mesmo host, duas tasks de provedores diferentes, cada
      `provider_calls` com o provedor da própria task. **Vermelho visto:** pico de
      chamadas simultâneas ao provedor `1` contra `2`. As duas chamadas se encontram
      numa barreira com prazo de 3 s; sem afirmar o pico, duas execuções em série
      passariam no guarda sem terem sido concorrentes (com o código atual os provedores
      já saíram certos, `[openai]` e `[anthropic]`, justamente por serem seriais).

## 3. Implementação (`apps/workers`)

- [x] 3.1 (`apps/workers`) `Options/TaskConsumerOptions.cs`: `SectionName =
      "TaskConsumer"`, `MaxConcurrentExecutions = 4`, constantes da faixa
      (`Min = 1`, `Max = 32`). XML doc: default **não medido**, gatilho de recalibração
      (`C` de pico, #48), motivo do teto (pool do Npgsql, D3) e rollback (`N = 1`).
- [x] 3.2 (`apps/workers`) `Messaging/TaskConsumerConcurrencyValidation.cs`:
      `ValidateTaskConsumerConcurrency(this IHost)`, no idioma de
      `ValidateEmbeddingBatchSize` (falha sem corrigir, linha de sucesso com o valor).
- [x] 3.3 (`apps/workers`) `Program.cs`: `Configure<TaskConsumerOptions>` vinculado à
      seção, e a chamada da checagem depois do `Build`, com o comentário de que nenhum
      teste prova o registro (D7).
- [x] 3.4 (`apps/workers`) `TaskJobConsumer`: injetar `IOptions<TaskConsumerOptions>`;
      criar o canal com `CreateChannelOptions(…, consumerDispatchConcurrency: N)` e
      chamar `BasicQosAsync` com `prefetchCount: N`. **Nada na `ConnectionFactory`.**
      Comentário ao lado da criação do canal: a pegadinha do construtor e o guarda 1.
- [x] 3.5 (`apps/workers`) `TaskJobConsumer.StopAsync`: sequência intacta (D5);
      reescrever o XML doc que afirma `prefetchCount: 1` para valer com `N` jobs.
- [x] 3.6 (`apps/workers`) Comentário de `KnowledgeIndexingConsumer` (D6): o motivo
      da fila própria continua, sem afirmar prefetch 1 do consumidor de tasks.
- [x] 3.7 (`apps/api`, só comentário) `KnowledgeIndexingQueues.cs`: mesma correção da
      3.6. Nenhum código de `apps/api` muda.
- [x] 3.8 (`apps/workers`, testes) Guardas da seção 2 verdes, e cada um volta a ficar
      vermelho contra a mutação que ele existe para pegar: guarda 1 com prefetch 3 **e**
      com a concorrência omitida do `CreateChannelOptions`; guarda 2 com `N = 1`;
      guarda 3 com o `nack` de parada removido. Registrar as mutações e o resultado
      inline (convenção 15).
      **Verdes:** os cinco guardas, 10/10 casos (o 5 tem seis), filtrados, 18/10/2026
      ~19:57, load 3,63. **Segunda perna** (`<scratchpad>/scripts/mutate.py`: aplica
      uma ocorrência exata, roda o filtro, restaura da cópia e confere o SHA-256;
      **CONFERE** nas seis):

      | mutação | guarda | resultado |
      |---|---|---|
      | `prefetchCount: N + 1` (3 com `N = 2`) | 1 | **vermelho**: `(2, 1, 0)`, a terceira sai da fila sem executar |
      | `consumerDispatchConcurrency` omitido | 1 | **vermelho**: `(1, 2, 1)` |
      | guarda com `N = 1` | 2 | **vermelho**: `(False, Completed, False)` |
      | `nack` da parada removido | 3 | **VERDE** |
      | `nack` da parada com `requeue: false` | 3 | **VERDE**, com os dois logs de interrupção emitidos |
      | `nack` removido, contra o guarda 2.2 da change da parada | `…RequeuesTheJobOnce` | **VERDE** |
      | ordem antiga da parada (canal fechado antes de `base.StopAsync`) | 3 | **vermelho**: 30,4 s |
      | `ExecutionMetricsScope` com o escopo em campo estático, não `AsyncLocal` (pedida na revisão) | 4 | **vermelho** por contaminação: `openai: []`, `anthropic: [anthropic, openai]`, pico 2 |

      **Divergência (convenção 9): a mutação pedida para o guarda 3 não muda o
      comportamento observável.** `Channel.CloseAsync` (7.2.1 decompilado) envia
      `channel.close` antes de esperar os callbacks; o broker devolve as entregas sem
      `ack`, e o `nack` sai com o canal fechado. O job volta à fila pelo fechamento, com
      ou sem `nack`, e nem `requeue: false` o descarta. Vale também para o guarda da
      change da parada, então é anterior a esta: **#177**. A segunda perna do guarda 3
      passou a ser a ordem antiga da parada, que é o defeito que ele de fato pega; a D5,
      a D7, o XML doc do `StopAsync` e o do guarda foram corrigidos para não afirmar
      que o `nack` devolve o job.

## 4. Harnesses que dependem de N = 1 (testes)

- [x] 4.1 (`apps/workers`, testes) Fixar `N = 1` explicitamente, com o motivo ao lado,
      em `TaskJobConsumerTests` (sentinela da linha 605),
      `ConversationContextLockFailureTests` (ponto de sincronização em `:186–191`, com
      o `prefetchCount: 1` na `:188`; era `:165–170` antes do XML doc que a #162 pôs
      no guarda 2), `AgentDelegationConcurrencyTests` e `AgentDelegationExecutionTests`,
      e nos que a tarefa 1.4 marcar.
- [x] 4.2 (`tests/InboxOrchestratorRoundTrip.Tests`) `RoundTripFixture` fixa `N = 1`
      dentro do `BuildWorkersHost`, e portanto nos três caminhos que o chamam (subida,
      `RestartWorkersAsync`, `StartSecondWorkersAsync`), **sem** virar controle da
      fixture (D7). Motivo escrito ao lado: a sentinela de
      `OrphanDispatchRoundTripTests` (`:108`) e a perna vermelha de
      `SessionSerializationRoundTripTests` (`:37–40`, `:69–75`). No mesmo teste,
      corrigir a referência `ConversationContextLockFailureTests.cs:167-172` (`:70`),
      que já estava velha na própria #162, para `:186–191`. E a docstring de
      `StartSecondWorkersAsync` (`RoundTripFixture.cs:327`, "as duas réplicas do
      piloto"), que é falsa: o piloto roda com três (D8); passa a não afirmar quantas.
- [x] 4.3 (`apps/workers`, `tests/InboxOrchestratorRoundTrip.Tests`, testes) Atualizar
      os comentários que afirmam `prefetchCount: 1` como fato de produção
      (`AgentDelegationConcurrencyTests`, `AgentDelegationExecutionTests`,
      `ExecutionMetricsScopeTests`, `ConversationContextLockFailureTests:188`,
      `SessionSerializationRoundTripTests:38`) para dizer que o harness fixa `N = 1`.
      **Feito, 4.1–4.3:** `N = 1` explícito, com o motivo ao lado, nos builders de
      `TaskJobConsumerTests` (os três, via `ConfigureTaskConsumer`),
      `AgentDelegationConcurrencyTests`, `AgentDelegationExecutionTests`,
      `ConversationContextLockFailureTests` e `RoundTripFixture.BuildWorkersHost` (os
      três caminhos). Além dos cinco listados, também `TaskJobConsumerTests:605` e
      `OrphanDispatchRoundTripTests:109` deixaram de afirmar prefetch 1 como fato. As
      duas correções de referência da 4.2 feitas (`SessionSerializationRoundTripTests:70`
      → `:186-191`, conferido depois das edições; docstring de `StartSecondWorkersAsync`
      sem número de réplicas). `git grep -i prefetch` nos testes não acha mais nenhuma
      afirmação de prefetch 1 de produção.
- [x] 4.4 (`apps/workers`, `tests/InboxOrchestratorRoundTrip.Tests`) Suítes inteiras
      contra a baseline da 1.1, por nome: todo teste da baseline continua existindo e
      passando, mais os novos. Números medidos, não de memória.
      **Resultado, 18/10/2026, mesmas condições da 1.1** (zero contêineres de dev, mesmas
      variáveis, mesmo script):

      | suíte | baseline | fechamento 1 (20:26) | fechamento 2 (21:20) |
      |---|---|---|---|
      | `apps/workers` | 405/405, 5m28s | **415/415**, 36m33s | **415/415**, 18m11s |
      | `InboxOrchestratorRoundTrip` | 10/11, depois 11/11 e 11/11 | **11/11**, 2m22s | **11/11**, 2m54s |

      **Por nome:** os 405 da baseline existem e passam; os novos são exatamente os 10
      dos guardas (`SingleInstanceWithTwoSlots_…`, os seis de
      `TaskConsumerConcurrencyValidationTests`, os três de
      `TaskJobConsumerConcurrencyTests`). Round-trip com os mesmos 11 nomes, todos
      verdes (`Source3`, da #176, passou nas duas). As duas rodadas de fechamento têm
      listas idênticas.
      **A duração do fechamento de `apps/workers` NÃO é medição** (régua da
      `WorkerHostCollection`, acima de ~10 min é contenção, e repeti). Soma dos corpos de
      teste: 381 s contra 237 s; o resto é subida de contêiner (~44 s antes de cada
      classe contra ~10 s, espalhados por igual; um buraco de 1201 s antes de
      `TaskJobConsumerTests` na primeira rodada, que não se repetiu na segunda).
      Condição medida: `pmset -g therm` com **`CPU_Speed_Limit = 23`** às 21:32, e o
      relógio da VM do Podman 27 min atrás do Mac (acertado à mão com `date -s`; voltou
      a atrasar ~2 min a cada 11). Não é da change: os buracos não se concentram nas
      classes que ela toca. Listas em `<scratchpad>/fechamento{,-3}/`.
      **Medição limpa, pedida na revisão (18/10/2026, 22:15–22:23):** antes de rodar,
      `CPU_Speed_Limit = 100`, relógio da VM acertado (desvio de 0 a 1 s em toda a rodada,
      amostrado a cada 30 s), zero contêineres e nenhum `vstest`/`dotnet run` além do
      próprio. `apps/workers` **415/415 em 6m27s** de parede (baseline 5m28s); soma dos
      corpos 281 s contra 237 s. Round-trip **10/11 em 50 s**: reprovou a `Source3` na
      `:112`, a mesma assinatura da #176 (agora 2 reprovações em 6 rodadas).
      **Por classe, a diferença ficou espalhada pelas classes antigas**, inclusive em
      classes que não sobem o `TaskJobConsumer` (`KnowledgeToolSetResolverTests` +5,5 s,
      `McpToolSetResolverTests` +5,0 s), com o maior aumento no **primeiro teste** de
      cada classe. Entre os nove harnesses sem `N` fixo, cinco subiram
      (`TemporalContextMessageInstantTests` +7,2 s, `ChannelContextMessageTests` +6,7 s,
      `AgentToolNamespaceTests` +5,8 s, `WorkerTests` +3,2 s, `ConversationHistoryTests`
      +3,1 s) e quatro não. **Experimento para separar a variável:** os nove harnesses
      isolados, com o default 4 (A) contra o default trocado para 1 (B), intercalados
      A, B, A, B, com checksum conferido nas duas restaurações. Soma dos corpos: A
      96,6 e 94,8 s; B 95,8 e 96,8 s; parede 146, 146, 142 e 143 s. A variação entre
      rodadas do mesmo lado é maior que a diferença entre os lados: **N = 4 não deixa
      esses harnesses mais lentos**. A regra da revisão (diferença espalhada por classes
      antigas → parar antes do commit) foi seguida: commit à espera do dono.
      **Baseline nas mesmas condições e investigação (pedidas na revisão, só com
      `--filter`, sem edição de código):**

      | rodada | árvore | testes | vstest | parede |
      |---|---|---|---|---|
      | baseline limpa (22:41) | `1e250e6`, worktree limpo | 405/405 | 5m39s | 5m45s |
      | B1 (23:06) | `1e250e6`, worktree limpo | 405/405 | 4m52s | 4m57s |
      | C1 (23:12) | change, sem os 10 testes novos | 405/405 | 5m12s | 5m17s |
      | C2 (23:17) | change, sem `TaskConsumerConcurrencyValidationTests` | 409/409 | 5m23s | 5m28s |
      | fechamento limpo (22:15) | change completa | 415/415 | 6m18s | 6m27s |

      Condições nas cinco, conferidas antes e amostradas a cada 30 s: `CPU_Speed_Limit =
      100`, relógio da VM acertado antes de cada rodada e com desvio de −1 a +1 s
      durante, zero contêineres, nenhum outro `vstest`/`testhost`/`dotnet run`, mesmas
      variáveis de ambiente, `--no-build` em todas menos o fechamento limpo (nele a
      parede inclui o build incremental; o tempo do vstest, não). A C1 bate com a B1 **nome a nome e estado a estado** (lista filtrada
      conferida antes de rodar).

      - **Resolvido:** o código da change não deixa testes antigos mais lentos. A C1
        (mesmos 405 nomes, árvore da change) roda em 5m12s, dentro da faixa da
        baseline (4m52s a 5m39s, duas rodadas nas mesmas condições).
      - **Não resolvido:** quanto do aumento da suíte completa (6m18s, uma amostra)
        vem de cada teste novo (classe de validação fora da coleção contra guardas
        dentro dela). Com um ruído de 47 s entre rodadas iguais, uma rodada de cada
        lado não separa isso. **Gatilho para retomar:** a suíte de `apps/workers`
        passar de 8 min em condições limpas, ou outra change medir crescimento que não
        venha dos próprios testes. **Primeira hipótese a testar:** mover
        `TaskConsumerConcurrencyValidationTests` para dentro da coleção de hosts.
      - **Ruído da suíte: 47 s** entre duas baselines nas mesmas condições. Dado para
        as próximas medições: afirmar diferença dessa ordem exige mais de uma rodada
        de cada lado.

## 5. Documentação

- [x] 5.1 (deploy) `docker-compose.prod.yml`: no comentário de Non-Goals, trocar só a
      justificativa de `apps/workers`/`apps/inbox` (D8) pelo motivo de cada um
      (`apps/workers`: disponibilidade, #48; `apps/inbox`: decisão operacional, #165);
      a parte do `apps/connectors` (#105) fica como está. Adicionar
      `TaskConsumer__MaxConcurrentExecutions: ${TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS:-4}`
      ao serviço `workers`, com comentário de default não medido e rollback.
- [x] 5.2 (docs) `docs/deployment.md` §4: mesma correção da 5.1, sem reescrever a
      frase inteira.
- [x] 5.3 (docs) `docs/development.md`: "duas instâncias" passa a "dois slots", com o
      caso de `N = 1`.
- [x] 5.4 (docs) `docs/configuration.md`: `TaskConsumer__MaxConcurrentExecutions` na
      tabela de `apps/workers` e `TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS` na de
      `.env.prod`, com default, faixa, "não medido", motivo do teto e rollback. E o
      gatilho da D3: o teto de 32 protege o pool do processo, e o limite que trava
      primeiro é o `max_connections` do servidor, dividido com `apps/api` e
      `apps/inbox`; **subir `N` acima do valor verificado na 6.1 exige refazer a 6.1
      antes.**
- [x] 5.5 (docs) `01`, "Filas de trabalho": `agent-tasks` com `N` slots por instância
      e prefetch = concorrência; motivo da fila de indexação reescrito.
- [x] 5.6 (docs) `02`: seção desta change (o que entrou, o que a implementação mediu,
      guardas, divergências); `replicas-de-worker` (#48) passa a tratar de
      disponibilidade; a fórmula conta slots; a contradição de réplicas marcada como
      resolvida; o Marco 2 da série de métricas passa a esta change (D9).
- [x] 5.7 (docs) `CHANGELOG.md`, em `[Unreleased]`.
- [x] 5.8 (docs) Script de integridade da documentação verde.

## 6. Antes do deploy (operação, do dono)

- [ ] 6.1 (deploy) Verificar o `max_connections` do Postgres de produção contra o pico
      real: `pg_stat_activity` por banco e por app, mais o pior caso de `apps/workers`
      com `N = 4` (D3) multiplicado pelas instâncias em operação no dia (três em
      18/10/2026, D8; reconferir com o dono no dia, não reaproveitar este número). Se
      não couber, o deploy espera. Registrar no `02` o `N`
      verificado: é o valor acima do qual a 6.1 precisa ser refeita.
- [x] 6.2 (deploy) Confirmar que a #162 (`inbox-serializacao-por-sessao`) está na
      `main` antes do merge desta, e rebasear esta sobre ela. **Feito em 18/10/2026:**
      a #162 está em `1e250e6` (PR #174) e esta branch foi rebaseada sobre ela
      (fast-forward, artefatos com checksum conferido). Falta, do dono, o deploy da
      #162 sozinha (marco 1, D9) antes do desta.
- [ ] 6.3 (deploy) Antes do deploy: decidir se a #166 sobe junto (marco 2 com as duas)
      ou se esta sobe sozinha (D9). Depois do deploy: linha da checagem de boot no log
      com o valor em vigor; marco das séries no `02` com data e fuso (D9), em data
      **posterior** ao marco da #162, nomeando as changes que entraram nele e com o
      aviso do deslocamento de espera da fila para o lock; taxa de 502 do gateway
      comparada à de antes.
