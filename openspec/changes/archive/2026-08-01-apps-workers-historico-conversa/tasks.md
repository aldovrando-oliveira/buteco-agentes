## 1. `apps/workers` — corrigir escopo de `ListTasksAsync`

- [x] 1.1 Em `apps/workers/src/Buteco.Workers/A2A/PostgresTaskStore.cs`, adicionar
      `.Where(task => task.AgentId == agentId)` à query de `ListTasksAsync`,
      usando o `agentId` já recebido no construtor (Decisão 2 do design.md).

## 2. `apps/api` — anotar a mesma lacuna (comentário apenas, sem mudança de comportamento)

- [x] 2.1 Em `apps/api/src/Buteco.Api/A2A/PostgresTaskStore.cs`, adicionar um
      comentário em `ListTasksAsync` apontando que a consulta não filtra por
      `AgentId` (mesma lacuna corrigida na cópia de `apps/workers`, task 1.1)
      — não urgente hoje por não ter consumidor nesta cópia, mas registrado
      para não ser esquecido se isso mudar (Decisão 2 do design.md). Nenhuma
      mudança de comportamento nesta task.

## 3. `apps/workers` — recuperar a sessão anterior do mesmo `contextId`

- [x] 3.1 Em `AgentExecutionService.ExecuteAsync`, antes de montar o
      `ChatClientAgent`, chamar `taskStore.ListTasksAsync(new ListTasksRequest
      { ContextId = message.ContextId, Status = TaskState.Completed, PageSize
      = 1 })` para obter a task `completed` mais recente do `contextId`
      (Decisão 3 do design.md).
- [x] 3.2 Se a task encontrada tiver `Metadata["conversationSession"]`,
      desserializar via `aiAgent.DeserializeSessionAsync(blob)`; caso
      contrário (nenhuma task encontrada, ou task sem esse campo — ex.
      gravada antes desta mudança), criar sessão nova via
      `aiAgent.CreateSessionAsync()` (Decisão 1 e Risks/Trade-offs do
      design.md).

## 4. `apps/workers` — limite de histórico

- [x] 4.1 Adicionar constante global (`MaxHistoryMessages`) em
      `AgentExecutionService`.
- [x] 4.2 Implementar `RecentMessageChatReducer` (`IChatReducer` próprio,
      não o `MessageCountingChatReducer` nativo — esse é
      `[Experimental("MEAI001")]`, ver Decisão 4 do design.md) e construir o
      `ChatClientAgent` via o overload de `ChatClientAgentOptions`,
      configurando `ChatHistoryProvider = new InMemoryChatHistoryProvider(new
      InMemoryChatHistoryProviderOptions { ChatReducer = new
      RecentMessageChatReducer(MaxHistoryMessages) })`.

## 5. `apps/workers` — persistir a sessão só no caminho de sucesso

- [x] 5.1 Passar a sessão (recuperada ou nova) para
      `aiAgent.RunAsync(userText, session, options: null, cancellationToken)`.
- [x] 5.2 Depois de `RunAsync` retornar com sucesso, antes do `SaveTaskAsync`
      final (mesmo passo que já faz `AddArtifactAsync`+`CompleteAsync`),
      atribuir `task.Metadata = new Dictionary<string, JsonElement> {
      ["conversationSession"] = await aiAgent.SerializeSessionAsync(session)
      }` no `AgentTask` projetado (Decisão 1 e 6 do design.md).
- [x] 5.3 Confirmar que o caminho de falha (`catch` existente, transição para
      `failed`) continua sem escrever `Metadata` (nenhuma mudança de código
      necessária ali — só confirmar que nenhum novo código foi adicionado a
      esse branch).

## 6. `apps/workers` — lock consultivo por `(agentId, contextId)`

- [x] 6.1 Adicionar um helper (ex. `Agents/ConversationContextLock.cs`) que
      abre uma conexão Postgres dedicada, adquire `pg_advisory_lock` com
      chave derivada de `(agentId, contextId)`, e expõe liberação via
      `IAsyncDisposable`/`await using` (Decisão 7 do design.md).
- [x] 6.2 Envolver a seção crítica de `ExecuteAsync` (leitura da sessão
      anterior → `RunAsync` → escrita da nova) com esse lock, garantindo
      liberação mesmo em exceção (`finally`/`await using`).

## 7. `apps/workers` — testes de integração (`Buteco.Workers.Tests`, `IChatClient` mockado)

- [x] 7.1 Histórico multi-turno **sem estado em memória entre instâncias**:
      construir **duas instâncias separadas** de `AgentExecutionService`
      (cada uma com seu próprio `IServiceScopeFactory`/container de DI
      montado do zero — nenhum objeto compartilhado entre elas além do
      Postgres do fixture de teste). Processar a primeira mensagem de um
      `contextId` pela instância A, aguardar `completed`, processar a
      segunda mensagem do mesmo `contextId` pela instância B (que nunca teve
      acesso a nenhum objeto/estado da instância A). Afirmar que o conteúdo
      passado ao mock na chamada da instância B inclui a mensagem e a
      resposta da primeira troca — provando que o mecanismo depende só do
      store durável compartilhado, não de cache/estado por instância (ver
      Scenario "Tasks do mesmo contextId processadas por instâncias
      diferentes..." em `specs/a2a-task-lifecycle/spec.md`). Um teste
      adicional processando as duas mensagens na mesma instância NÃO é
      necessário à parte — o cenário de duas instâncias já cobre o caso mais
      simples (mesma instância) por construção.
- [x] 7.2 Limite de histórico: mais mensagens acumuladas que
      `MaxHistoryMessages` → só as mais recentes chegam ao mock.
- [x] 7.3 Task anterior `failed` no mesmo `contextId` não aparece no
      histórico da task seguinte.
- [x] 7.4 **Instructions atualizadas em conversa em andamento** (Decisão 5 do
      design.md): processar uma primeira mensagem de um `contextId` com um
      agente cujo `Instructions` é X; atualizar o agente no banco para
      `Instructions` Y (simulando `PUT /agents/{id}`) entre as duas
      mensagens; processar a segunda mensagem do mesmo `contextId`; afirmar
      que o `ChatOptions.Instructions`/parâmetro equivalente recebido pelo
      mock na segunda chamada é Y, não X — confirmando empiricamente que a
      sessão desserializada não prende um system prompt antigo.

## 8. `tests/CrossAppTaskStoreCompatibility.Tests` — estender cobertura

- [x] 8.1 Adicionar cenário em `PostgresTaskStoreCompatibilityTests`: task
      `completed` com `Metadata` populado (incluindo
      `conversationSession`), escrita pelo `PostgresTaskStore` de um app,
      lida corretamente (campo presente, mesmo conteúdo) pelo do outro app,
      nos dois sentidos (Decisão 8 do design.md).

## 9. `apps/workers` — retry em `GetTaskAsync` (achado via teste manual, Decisão 9 do design.md)

- [x] 9.1 Em `AgentExecutionService.ExecuteAsync`, substituir a chamada única
      a `taskStore.GetTaskAsync` por um retry curto (`GetTaskWithRetryAsync`
      — até 5 tentativas, 100ms entre elas) antes de logar erro e desistir,
      absorvendo a corrida entre `apps/api` publicar no RabbitMQ e o
      `SaveTaskAsync` do evento "submitted" ainda não ter commitado.
- [x] 9.2 Teste de integração em `Buteco.Workers.Tests`
      (`TaskJobConsumerTests`): publicar o job antes da task existir no
      store, inserir a task só depois de um atraso, e confirmar que o worker
      tenta de novo e processa com sucesso em vez de descartar a mensagem.
- [x] 9.3 **Segunda camada da mesma corrida, achada testando o próprio fix**:
      `GetTaskWithRetryAsync` passa a exigir task encontrada **e** com uma
      mensagem de usuário utilizável (`ExtractLatestUserText` não vazio) —
      `EnqueueingAgentHandler` grava a task em dois eventos separados
      (`SubmitAsync` cria a linha, `EnqueueMessageAsync` grava a mensagem
      depois), e o retry original (só "task existe") ficava mais propenso a
      pegar a janela entre os dois do que o código sem retry.
- [x] 9.4 Teste de integração em `Buteco.Workers.Tests`
      (`TaskJobConsumerTests`): publicar o job com a task já criada mas sem
      `History`, adicionar a mensagem do usuário só depois de um atraso, e
      confirmar que o worker espera e processa com sucesso em vez de chamar
      `RunAsync` com mensagem vazia.

## 10. `apps/workers` — codificar a sessão como string JSON escapada (achado via teste manual, Decisão 10 do design.md)

- [x] 10.1 Criar `ConversationSessionCodec` (`Agents/ConversationSessionCodec.cs`)
      com `Encode(JsonElement) -> JsonElement` (reencoda via
      `JsonSerializer.SerializeToElement(serializedSession.GetRawText())`) e
      `Decode(JsonElement) -> JsonElement` (`JsonDocument.Parse(valor.GetString())`).
- [x] 10.2 Usar `ConversationSessionCodec.Encode`/`.Decode` em
      `AgentExecutionService` no lugar de guardar/ler o `JsonElement` da
      sessão serializada diretamente em `Metadata["conversationSession"]`.
- [x] 10.3 Atualizar `BuildSampleConversationSession` em
      `PostgresTaskStoreCompatibilityTests` para usar
      `ConversationSessionCodec.Encode`, refletindo o formato real persistido
      (string escalar, não objeto aninhado).
- [x] 10.4 Teste unitário (`ConversationSessionCodecTests`, sem Postgres):
      serializar uma sessão real com conteúdo polimórfico, simular a
      reordenação de propriedades que o `jsonb` faz, e confirmar que (a) sem
      o codec a desserialização quebra e (b) com o codec sobrevive e
      recupera o conteúdo original.
