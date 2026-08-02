## Why

A change `apps-workers-historico-conversa` já faz `apps/workers` incluir o
histórico de conversa do mesmo `contextId` em toda chamada ao LLM, limitado a
`MaxHistoryMessages` (20) mensagens brutas por truncamento simples
(`RecentMessageChatReducer`). Truncar descarta contexto sem aviso assim que
uma conversa passa desse limite — turnos antigos somem por completo em vez de
serem condensados. Para conter custo/latência por turno sem perder esse
contexto à medida que uma conversa cresce, o histórico que excede um limiar
de interações deve ser resumido (não descartado) antes de entrar na chamada
ao LLM.

## What Changes

- `apps/workers` passa a resumir automaticamente a porção mais antiga do
  histórico de conversa de um `contextId` depois que o número de interações
  (turnos de usuário) ultrapassa um limiar global, em vez de só truncar.
- A chamada ao LLM para o turno corrente passa a incluir o resumo condensado
  + os turnos mais recentes preservados, no lugar do histórico cru completo
  correspondente à porção resumida.
- O resumo é recalculado de forma incremental (a cada novo cruzamento do
  limiar, soma o resumo anterior aos turnos novos desde então) — nunca
  resume a conversa inteira do zero a cada gatilho.
- Falha na chamada de resumo (ex.: provider fora do ar) não impede o turno
  do usuário de ser processado — o turno segue com o histórico não resumido
  daquele momento, e o resumo é tentado novamente no próximo gatilho.
- `RecentMessageChatReducer`/`InMemoryChatHistoryProvider` continuam
  limitando o que é persistido no Postgres — o mecanismo não muda, mas o
  valor do teto (`MaxHistoryMessages`) é revisado (de 20 para um teto bem
  mais alto, ver design.md Decisão 9) para não competir com o resumo novo:
  investigação decompilando o pipeline real de `ChatClientAgent` confirmou
  que o truncamento roda antes do resumo em toda chamada, e que os valores
  originalmente próximos entre os dois mecanismos invalidariam o
  bookkeeping incremental do resumo assim que a conversa cresce o
  suficiente para truncar. O resumo novo atua sobre o que é enviado ao LLM
  em cada chamada, via um `AIContextProvider` adicional.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `a2a-task-lifecycle`: o requisito "Workers processam a task até um estado
  terminal" ganha comportamento novo — histórico que ultrapassa um limiar de
  interações é resumido (não só truncado) antes da chamada ao LLM, de forma
  incremental, com degradação graciosa se a chamada de resumo falhar.

## Impact

- **apps/workers** (único app afetado — non-goal explícito: nenhuma mudança
  em `apps/api` ou `apps/frontend`):
  - `Agents/AgentExecutionService.cs`: `ChatClientAgentOptions` passa a
    incluir `AIContextProviders` com um `CompactionProvider` novo, usando
    `SummarizationCompactionStrategy` (`Microsoft.Agents.AI.Compaction`,
    pacote `Microsoft.Agents.AI` 1.15.0 já instalado — sem bump de versão),
    reaproveitando o `IChatClient` já resolvido pelo `IChatClientResolver`
    para o agente. Nova constante global para o limiar de interações
    (mesmo estilo de `MaxHistoryMessages` hoje).
  - Nenhuma tabela, coluna ou migration nova — o estado incremental do
    resumo é persistido dentro do `AgentSession.StateBag` já serializado em
    `AgentTask.Metadata["conversationSession"]` (mesma coluna `a2a_tasks`
    jsonb, mesmo `ConversationSessionCodec`, ambos da change
    `apps-workers-historico-conversa`).
  - Dependência de pacote: nenhuma nova — `Microsoft.Agents.AI.Compaction`
    já vem dentro do pacote `Microsoft.Agents.AI` 1.15.0 já referenciado por
    `apps/workers`. A API usada é `[Experimental("MAAI001")]` — risco
    aceito e documentado em `design.md`.
  - Testes: `Buteco.Workers.Tests` ganha cobertura nova com `IChatClient`
    mockado (mesmo padrão já estabelecido), sem depender de Testcontainers
    para os cenários que não exigem Postgres real.
