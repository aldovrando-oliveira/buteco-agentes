## 1. apps/inbox — escrever o instante da mensagem no SendMessage

- [x] 1.1 (`apps/inbox`) Em `DebounceSweepService.BuildSendMessageRequest`, adicionar `Metadata` ao `Message` construído, com a chave `messageInstant` recebendo `pendingDispatch.LastMessageAt` serializado como string ISO 8601 com offset.
- [x] 1.2 (`apps/inbox`) Escrever o helper de serialização (`JsonSerializer.SerializeToElement(value, A2AJsonUtilities.DefaultOptions)`) como método privado/estático local a `DebounceSweepService` — não extrair para `libs/`, é duplicado deliberadamente em `apps/workers` (ver Tarefa 3).
- [x] 1.3 (`apps/inbox`) Teste: `SendMessage` disparado inclui `Message.Metadata["messageInstant"]` igual a `PendingDispatch.LastMessageAt`, para um buffer com uma mensagem.
- [x] 1.4 (`apps/inbox`) Teste: `SendMessage` disparado para um buffer com múltiplas mensagens usa o `LastMessageAt` (última mensagem), não o instante da primeira.

## 2. apps/workers — ler o instante da mensagem e resolver a precedência

- [x] 2.1 (`apps/workers`) Em `AgentExecutionService`, junto ao ponto onde a última mensagem do usuário já é extraída do histórico da task (`ExtractLatestUserText`/`GetTaskWithRetryAsync`), adicionar extração de `Message.Metadata["messageInstant"]` dessa mesma mensagem.
- [x] 2.2 (`apps/workers`) Implementar o parse defensivo: `Metadata` nulo, chave ausente, ou valor não parseável como ISO 8601 → tratar como sem instante de mensagem (retornar `null`), sem lançar exceção.
- [x] 2.3 (`apps/workers`) Para o terceiro caso (chave presente, valor ilegível), emitir log de nível aviso com o identificador da task afetada.
- [x] 2.4 (`apps/workers`) Repassar o `DateTimeOffset?` extraído para `TemporalContextBlockBuilder.Build(timeProvider, messageInstant)` no ponto de montagem já existente — sem alterar a assinatura de `Build` (já aceita o parâmetro desde a etapa 1).
- [x] 2.5 (`apps/workers`) Teste: task cuja última mensagem do usuário tem `messageInstant` válido → bloco de contexto temporal inclui esse instante e a regra de precedência resolve contra ele.
- [x] 2.6 (`apps/workers`) Teste: task sem `Message.Metadata`, sem a chave, e com valor ilegível (três casos) → bloco de contexto temporal idêntico ao comportamento da etapa 1 (resolve contra o instante de processamento), sem falhar a task.
- [x] 2.7 (`apps/workers`) Teste: valor ilegível → log de aviso emitido com o identificador da task.
- [x] 2.8 (`apps/workers`) Teste: par de defasagem acima/abaixo do limiar configurado (constante já existente da etapa 1), agora com `messageInstant` real — confirmar que a linha de defasagem aparece só acima do limiar, pela primeira vez em um cenário com os dois instantes definidos.

## 3. apps/workers — propagar o instante da mensagem na delegação

- [x] 3.1 (`apps/workers`) Alterar a assinatura de `IAgentDelegationToolSetResolver.ResolveAsync` para aceitar um parâmetro `DateTimeOffset? messageInstant`.
- [x] 3.2 (`apps/workers`) Em `AgentExecutionService.ExecuteAsync`, passar para `ResolveAsync` o mesmo `messageInstant` já extraído na Tarefa 2.1 para a própria task do Source.
- [x] 3.3 (`apps/workers`) Em `AgentDelegationToolSetResolver`, capturar `messageInstant` na closure de `BuildDelegationTool`, junto com `contextId`/`currentDepth` (já capturados hoje).
- [x] 3.4 (`apps/workers`) Repassar `messageInstant` de `DelegateToTargetAsync` para `CreateDelegatedTaskAsync`.
- [x] 3.5 (`apps/workers`) Em `CreateDelegatedTaskAsync`, gravar `messageInstant` (quando não nulo) em `Message.Metadata["messageInstant"]` do `Message` inicial construído para o Target, usando o mesmo helper de serialização da Tarefa 1.2 (duplicado aqui, não compartilhado — ver Decisão D2 do `design.md`).
- [x] 3.6 (`apps/workers`) Teste: task do Source com `messageInstant` disponível, delegando para um Target → task criada para o Target tem a mesma chave/valor em `Message.Metadata`. **Este teste sozinho não prova entrega** — só prova que a chave foi escrita, não que o Target a lê corretamente; quem prova isso é a Tarefa 3.8, obrigatória, não alternativa a esta (ver invariante nomeado na Decisão D3 do `design.md`).
- [x] 3.7 (`apps/workers`) Teste: task do Source sem `messageInstant` disponível, delegando para um Target → task criada para o Target não tem a chave `messageInstant` (não inventa um valor).
- [x] 3.8 (`apps/workers`) Teste de divergência: Source processado em um instante de processamento simulado T1, Target processado (via `TimeProvider` controlável) em um instante de processamento simulado T2 posterior — ambos resolvem a mesma expressão relativa ("amanhã") para a mesma data, usando o `messageInstant` propagado. Sem este teste, a Decisão D3 do `design.md` entra sem contraparte.

## 4. Teste de acordo entre apps/inbox e apps/workers (convenção 11)

- [x] 4.1 (`tests/` raiz — `InboxOrchestratorRoundTrip.Tests`, que já exercita o round-trip completo `apps/inbox` → `apps/api` → `apps/workers`) Adicionar asserção sobre o valor de `messageInstant`: disparar um `SendMessage` real produzido por `apps/inbox` (Tarefa 1) e confirmar, do lado de `apps/workers`, o **JSON bruto** persistido/consumido (`GetRawText()` ou equivalente, não round-trip pelo mesmo tipo C#) — mesmo padrão do teste de enums de `inbox-enums-json-string`. Um fixture forjado nas duas pontas com a mesma configuração passaria igual com o formato certo e com o errado, e por isso não prova nada (convenção 11). **Implementado e compila** (`MessageReceived_TriggersFullRoundTrip_TaskCarriesMessageInstantInRawPersistedJson`); **não verificado passando nesta sessão** — o teste-irmão já existente (`MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload`), sem nenhuma mudança de código, também não completou o round-trip neste ambiente (confirmado isolando-o, inclusive com timeout estendido a 90s sem sucesso) — falha de ambiente pré-existente, não regressão desta change. Ver Registro/Verificação final.

## 5. Registro (parte da change, não passo separado)

- [x] 5.1 (`02-HISTORICO_E_STATUS.md`) Corrigir o registro do custo dimensionado da etapa 2: `TaskJobMessage` **não** precisa de campo novo — `apps/api` já persiste `Message.Metadata` sem mudança de código, e `apps/workers` já relê a task inteira do store. Registrar com a evidência (rastreamento do pipeline `EnqueueingAgentHandler` → `TaskProjection.Apply` → `PostgresTaskStore.SaveTaskAsync`).
- [x] 5.2 (`02-HISTORICO_E_STATUS.md`) Corrigir o registro do custo da propagação na delegação: não são 2 pontos, são 4 métodos (`ResolveAsync`, `AgentExecutionService.ExecuteAsync`, `BuildDelegationTool`, `DelegateToTargetAsync`) + `CreateDelegatedTaskAsync` (que a exploração havia marcado como dispensável, mas precisa gravar a chave no `Message` do Target) + 1 assinatura de interface.
- [x] 5.3 (`02-HISTORICO_E_STATUS.md`) Registrar como change aplicada, encerrando a linha de contexto temporal e de canal (etapa 2 de 2 — instante da mensagem; contexto de canal segue como `inbox-contexto-canal`, ainda não proposta).
- [x] 5.4 (`02-HISTORICO_E_STATUS.md`, Itens em aberto) Registrar item novo: `WahaInboundWebhookHandler` e `TelegramInboundWebhookHandler` chamam `DateTimeOffset.UtcNow` direto, fora do `TimeProvider` — mesma regra que o Achado 9 fechou em `apps/workers`, mostrando que aquela varredura era só de `apps/workers`. Gatilho: antes de qualquer mudança futura nesses adapters, ou se algum teste precisar de relógio determinístico em `apps/inbox`.
- [x] 5.5 (`02-HISTORICO_E_STATUS.md`, Itens em aberto) Registrar item novo: nenhum dos dois adapters desserializa o timestamp que o provedor (WAHA/Telegram) envia no payload — `Message.OccurredAt` é sempre o instante de recebimento do webhook. Gatilho: se a defasagem por atraso do provedor virar problema real observado.

Sem tarefa de adicionar exemplo à convenção 12 de
`01-ARQUITETURA_E_CONVENCOES.md` — avaliado e descartado, decisão e
motivo escritos na Decisão D2 do `design.md` (o mecanismo de `JsonElement`
já está documentado na seção "AgentCard / protocolo A2A" do mesmo
arquivo; esta change aplica esse conhecimento, não descobre uma lição
nova).

## 6. Verificação final

- [x] 6.1 (`apps/inbox`) Suíte completa de `Buteco.Inbox.Tests` verde, sem regressão sobre a baseline nomeada mais recente (`02-HISTORICO_E_STATUS.md`). **162/162** (160 da baseline + 2 novos desta change, `DebounceMessageInstantTests`).
- [x] 6.2 (`apps/workers`) Suíte completa de `Buteco.Workers.Tests` verde, sem regressão sobre a baseline nomeada mais recente. **102/102** (92 da baseline + 10 novos desta change: 7 em `TemporalContextMessageInstantTests`, 3 em `AgentDelegationExecutionTests`).
- [x] 6.3 (`tests/` raiz) `InboxOrchestratorRoundTrip.Tests` e `CrossAppTaskStoreCompatibility.Tests` executados. `CrossAppTaskStoreCompatibility.Tests`: **2/2**, sem mudança. `InboxOrchestratorRoundTrip.Tests`: 1/3 — só `OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi` passou; os outros dois (`MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload`, já existente, sem nenhuma mudança de código, e o novo desta change,
  `MessageReceived_TriggersFullRoundTrip_TaskCarriesMessageInstantInRawPersistedJson`)
  falharam com o **mesmo** `TimeoutException`, **por nome, não por
  contagem**. Confirmado com evidência direta, não por semelhança: `git
  worktree` isolado no commit imediatamente anterior ao apply (`a91f0c9`,
  mesmo método do bisect de `crossapp-session-codec-encoder`), timeout
  estendido a 90s dos dois lados — o commit base falha **identicamente**
  ao HEAD desta change (~91s, mesmo `TimeoutException`), não regressão. O
  registro anterior de "~21-22s contra limite de 20s" nunca foi evidência
  de proximidade do sucesso — era só o próprio limite interno do teste
  cortando a espera cedo; o round-trip não completa, com ou sem esta
  change, neste ambiente/sessão. Registrado em "Itens em aberto" do
  `02-HISTORICO_E_STATUS.md` (dois itens: o timeout em si, corrigido para
  refletir a degradação real; e um item novo — o teste de acordo da
  convenção 11 desta change nunca foi executado com sucesso, então o
  formato de fio de `messageInstant` não foi provado ponta a ponta nesta
  sessão, só por asserções unitárias).
