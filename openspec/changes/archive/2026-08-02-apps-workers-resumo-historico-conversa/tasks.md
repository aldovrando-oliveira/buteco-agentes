## 1. `apps/workers` — configuração global do limiar de resumo e reconciliação com `MaxHistoryMessages`

- [x] 1.1 Em `apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`,
      adicionar constante global `SummarizationTurnThreshold` (mesmo estilo
      de `MaxHistoryMessages` — não configurável por agente, Non-Goal
      explícito do design.md), ponto de partida sugerido: 10 interações
      (Open Questions do design.md).
- [x] 1.2 Revisar o valor de `MaxHistoryMessages` (hoje 20) para um teto de
      segurança bem mais alto — ponto de partida sugerido: 200 (Decisão 9 e
      Open Questions do design.md). Não é uma mudança no mecanismo
      `RecentMessageChatReducer`/`InMemoryChatHistoryProvider` (que
      continua igual) — só no valor da constante, para que ele pare de
      competir com `SummarizationTurnThreshold` e funcione como rede de
      segurança contra crescimento patológico.

## 2. `apps/workers` — plugar `CompactionProvider`/`SummarizationCompactionStrategy` no `ChatClientAgent`

- [x] 2.1 Em `AgentExecutionService.ExecuteAsync`, ao montar
      `ChatClientAgentOptions` (onde `ChatHistoryProvider` já é configurado
      com `RecentMessageChatReducer`, sem alterar isso), adicionar
      `AIContextProviders` com um `Microsoft.Agents.AI.Compaction.CompactionProvider`
      novo, construído com `new SummarizationCompactionStrategy(chatClient,
      CompactionTriggers.TurnsExceed(SummarizationTurnThreshold))` — Decisões
      1, 4 e 7 do design.md.
- [x] 2.2 Reaproveitar a mesma variável `chatClient` já obtida por
      `chatClientResolver.Resolve(agent.Provider!, agent.Model!)` nessa
      mesma execução — sem resolver nem construir um `IChatClient` separado
      para a chamada de resumo (Decisão 4 do design.md).
- [x] 2.3 Suprimir o diagnóstico `[Experimental("MAAI001")]` no ponto de uso
      (ex. `#pragma warning disable MAAI001` isolado ao trecho de construção
      de `ChatClientAgentOptions`, ou `<NoWarn>` restrito ao arquivo/projeto,
      conforme o padrão já usado no repo para diagnósticos experimentais
      aceitos — conferir como `MEAI001`/`MAAI001` já são tratados em outros
      pontos do código antes de escolher o mecanismo).

## 3. `apps/workers` — testes de integração (`Buteco.Workers.Tests`, `IChatClient` mockado)

- [x] 3.1 Abaixo do limiar: processar menos interações que
      `SummarizationTurnThreshold` no mesmo `contextId` e confirmar que o
      mock nunca recebe uma chamada adicional de resumo — comportamento
      idêntico ao da change anterior, sem regressão.
- [x] 3.2 Ao cruzar o limiar: processar interações suficientes para
      ultrapassar `SummarizationTurnThreshold` no mesmo `contextId` e
      confirmar (a) que uma chamada de resumo é feita ao mock antes da
      chamada de conversa real do turno que cruza o limiar, e (b) que essa
      chamada de conversa real recebe o resumo + os turnos mais recentes
      preservados no lugar do histórico cru completo da porção mais antiga
      (Scenario "Histórico que ultrapassa o limiar de interações é resumido
      em vez de descartado", `specs/a2a-task-lifecycle/spec.md`).
- [x] 3.3 Resumo incremental através de muitos turnos, dentro da faixa de
      segurança de `MaxHistoryMessages` (Decisão 9 do design.md — este é o
      teste que teria pego a regressão achada via `/opsx:explore` antes do
      apply: `CompactionMessageIndex.Update` descarta todo o bookkeeping
      acumulado sempre que `RecentMessageChatReducer` trunca a lista de
      mensagens que o alimenta, então um teste de só duas rodadas de resumo
      não é suficiente para provar estabilidade). Processar turnos
      suficientes para cruzar `SummarizationTurnThreshold` **múltiplas
      vezes** (pelo menos 3-4 gatilhos) na mesma conversa, mantendo o total
      de mensagens brutas bem abaixo do novo `MaxHistoryMessages`, e
      confirmar que: (a) cada nova chamada de resumo recebe o resumo
      anterior + só os turnos novos desde então, nunca a conversa inteira
      do zero (Scenario "Resumo é recalculado de forma incremental..."); e
      (b) o conteúdo do primeiro resumo (gerado no primeiro gatilho) ainda
      está refletido/incorporado no resumo mais recente muitos turnos
      depois — provando que o estado do `CompactionProvider` não é
      resetado silenciosamente ao longo da conversa.
- [x] 3.4 Falha na chamada de resumo: configurar o mock para lançar exceção
      quando invocado para gerar o resumo, cruzar o limiar, e confirmar que
      (a) a task do turno corrente ainda assim chega a `completed` com o
      histórico não resumido daquele momento, e (b) a task não é marcada
      `failed` por causa exclusivamente dessa falha (Scenario "Falha na
      chamada de resumo não impede o turno do usuário de ser processado").
- [x] 3.5 Round-trip de sessão: aplicar compactação/resumo, serializar a
      sessão (`SerializeSessionAsync`) e desserializar de volta
      (`DeserializeSessionAsync`, mesmo `ConversationSessionCodec` já
      existente), e confirmar que o estado incremental do
      `CompactionProvider` sobrevive — uma execução seguinte não resume tudo
      de novo do zero (mesmo espírito da Decisão 10/`ConversationSessionCodecTests`
      da change anterior, mas focado no estado do `CompactionProvider`, não
      no `AIContent` polimórfico já coberto lá).
- [x] 3.6 Degradação além do teto de segurança (Decisão 9/Risks do
      design.md): processar turnos suficientes para que o total de
      mensagens brutas ultrapasse o novo `MaxHistoryMessages`, forçando
      `RecentMessageChatReducer` a truncar ativamente, e confirmar que (a)
      o worker não lança exceção nem falha a task por causa disso — o
      comportamento degrada para truncamento puro (mesmo comportamento
      pré-esta-change), sem crash; (b) isso é tratado como o limite
      conhecido e aceito, não como bug — o teste documenta o comportamento
      esperado no caso extremo, não tenta "corrigi-lo".

## 4. Verificação final

- [x] 4.1 Rodar a suíte de testes de `apps/workers`
      (`dotnet test apps/workers/tests/Buteco.Workers.Tests`) e confirmar que
      os testes existentes da change anterior (histórico, limite de
      mensagens, Instructions atualizadas, retry) continuam passando sem
      alteração. **Nota**: neste ambiente de implementação, Docker não está
      disponível — todos os testes que dependem de `WorkerInfrastructureFixture`
      (Testcontainers Postgres+RabbitMQ), tanto os pré-existentes quanto os
      novos desta change, falham com `DockerUnavailableException`. Os 9
      testes que não dependem de containers passam. Compilação (`dotnet
      build`, produção e testes) está limpa, 0 avisos/erros. Execução real
      da suíte com Docker fica pendente de verificação manual/CI — mesma
      limitação já documentada no design.md da change anterior para testes
      de integração com Testcontainers.
