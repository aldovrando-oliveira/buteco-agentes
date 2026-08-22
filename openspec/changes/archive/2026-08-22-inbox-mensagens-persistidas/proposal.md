## Why

`apps/inbox` não persiste nenhuma mensagem hoje. O único registro que existe
por sessão é `PendingDispatch` — um buffer de debounce, não histórico — e ele
é removido assim que o ciclo de disparo termina (sucesso, rejeição ou falha
definitiva), levando o conteúdo junto. A resposta de saída do agente também
não é gravada em lugar nenhum além do `IOutboundMessageSender` de destino.
Sem um registro durável de mensagens por `Session`, a etapa 3 desta linha de
trabalho (tela de operador mostrando o que aconteceu numa conversa) não tem o
que exibir — e hoje, se o agente não responde, a sessão mostra uma mensagem
de entrada e depois nada, sem forma de distinguir debounce em curso, falha de
dispatch, falha de task ou falha de envio.

## What Changes

- Nova entidade `Message` (`apps/inbox`), tabela relacional própria ligada a
  `Session`: direção (entrada/saída), conteúdo, tipo de conteúdo (marcador —
  `Text`/`Image`/`Audio`/`Document`, sem persistir mídia binária), instante,
  identificador externo (entrada, para deduplicar webhook reentregue), status
  de entrega e motivo de falha (saída — sucesso/falha do envio ao provedor,
  não recibo de entrega/leitura).
- Persistência de mensagem de entrada centralizada em
  `InboundMessageOrchestrator` (ponto único já existente onde os dois
  adapters convergem) — não duplicada em cada `IInboundWebhookHandler`.
  `IInboundMessageOrchestrator.ReceiveMessageAsync` e os dois handlers
  (`WahaInboundWebhookHandler`, `TelegramInboundWebhookHandler`) passam a
  carregar o identificador externo de mensagem necessário para a
  deduplicação — payloads de WAHA e Telegram ganham esse campo.
- Persistência de mensagem de saída centralizada em
  `PushNotificationEndpoints.DeliverResponseAsync` (ponto único já existente
  que invoca `IOutboundMessageSender.SendAsync`) — contrato do plugin não
  muda.
- Estado de dispatch renderável: mensagens de entrada carregam uma
  correlação opcional (sem FK, já que a linha é removida) com o
  `PendingDispatch` que as consumiu, e um status próprio de quatro estados
  (`Pending`/`Dispatching`/`Failed`/`Completed`) espelhado pelos mesmos
  pontos que hoje mutam ou removem `PendingDispatch`
  (`DebounceSweepService`, `PushNotificationEndpoints`) — sem alterar
  `PendingDispatch` nem sua coleção owned/JSON `Messages`.
- `Contact.DisplayName` (nullable, novo), extraído do WAHA e do Telegram,
  atualizado a cada mensagem de entrada — comportamento distinto do
  `Contact.Metadata` existente (congelado na criação).
- Consulta de mensagens de uma `Session`, em ordem cronológica, para
  consumo futuro pela UI da etapa 3 (fora de escopo aqui).
- Consulta de sessões de um `Channel` por última atividade, com prévia da
  última mensagem de cada sessão e o `DisplayName`/`ExternalId` do
  `Contact` — a outra consulta que a etapa 3 precisa (tela de entrada, antes
  de abrir qualquer sessão), que hoje não existe (`GetContactSessions` é por
  `Contact`, não por `Channel`).
- Correção textual da seção *Risks* de
  `openspec/changes/archive/2026-08-16-inbox-fix-concorrencia-orquestrador/design.md`,
  que ainda descreve o mecanismo de reload abandonado (`ReloadAsync`) em vez
  do mecanismo final (detach + rebusca) — débito da mesma pendência de
  varredura fechada por esta change.

## Capabilities

### New Capabilities
- `inbox-message-history`: persistência durável de mensagens de entrada e
  saída por `Session` em `apps/inbox` — entidade `Message`, deduplicação por
  identificador externo, status de entrega de saída, estado de dispatch
  renderável espelhado do buffer de debounce, e consulta cronológica por
  sessão.

### Modified Capabilities
- `inbox-message-orchestration`: a ingestão de mensagem normalizada passa a
  exigir um identificador externo de mensagem (usado pela deduplicação da
  nova capability) além dos campos já aceitos.
- `inbox-contact-session`: `Contact` ganha `DisplayName`, capturado e
  atualizado a cada mensagem de entrada — diferente da captura única de
  `Metadata` na criação; ganha também a consulta de sessões de um canal por
  última atividade, com prévia da última mensagem (lê `inbox-message-history`).

## Impact

- **apps/inbox**: nova entidade/tabela `Message` + migration; mudança de
  assinatura em `IInboundMessageOrchestrator.ReceiveMessageAsync` e nos dois
  `IInboundWebhookHandler` (WAHA, Telegram); novo mutator em `Contact`;
  escrita adicional em `DebounceSweepService` e `PushNotificationEndpoints`
  (sem tocar `PendingDispatch.cs` nem sua coleção owned/JSON); duas novas
  consultas (serviço/endpoint) — mensagens por sessão, e sessões de um canal
  com prévia. Sem mudança em `apps/api`, `apps/workers` ou `apps/frontend`.
