## Why

`PushNotificationEndpoints.DeliverResponseAsync` (`apps/inbox/.../PushNotificationEndpoints.cs:121-176`) resolve o `Channel` de origem (consulta nas linhas 134-140) e chama `credentialCipher.Decrypt(dispatchInfo.EncryptedCredentials)` (linha 148) **antes** do `try/catch` que existe especificamente para engolir falha de entrega ao canal (a partir da linha 154). Se essa consulta ou a decifragem lançarem, a exceção escapa do handler inteiro: `apps/inbox` devolve 500 não tratado, `PushNotificationSender` (`apps/workers`) — fire-and-forget, sem retry, por decisão deliberada de `a2a-push-notifications` (Decision 3) — só loga um aviso e desiste, e o `PendingDispatch` nunca é limpo. A mensagem do usuário fica presa em "Processando" para sempre, depois de a resposta do agente já ter sido gerada (já custou uma chamada de LLM).

Não é um problema de teste isolado: em produção, `Decrypt` pode falhar por rotação da chave AES-GCM, credencial gravada com uma chave anterior, ou dado corrompido — falhas plausíveis, não hipotéticas. É o mesmo defeito estrutural já corrigido em `DebounceSweepService` por `inbox-sweep-service-resiliencia` (convenção 4: chamada capaz de falhar posicionada fora do bloco protegido), agora encontrado num terceiro componente. A causa foi confirmada por execução real e par de commits nomeados (`c72c64f` passa em 1s, `9fd8a87` — a correção que fez `DeliverResponseAsync` executar pela primeira vez — quebra), durante a exploração `roundtrip-tres-apps-nao-completa`, que também é o motivo de `tests/InboxOrchestratorRoundTrip.Tests` estar parado desde então.

## What Changes

- Ampliar o bloco protegido de `DeliverResponseAsync` para cobrir a consulta que resolve `dispatchInfo` (Channel/Contact via Session) e a chamada a `credentialCipher.Decrypt`, não só `sender.SendAsync` — mesma captura ampla por unidade de trabalho já usada ali, sem lista fechada de tipos de exceção (precedente: `inbox-sweep-service-resiliencia`, Decisão 3).
- Ajustar a mensagem de log de erro do `catch` para não depender de `dispatchInfo` (pode não existir se a própria consulta falhar) — identificar a falha por `pendingDispatch.SessionId`, disponível em qualquer ponto do bloco.
- Nenhuma mudança em `MessageDispatchStatus`/remoção de `PendingDispatch`: o comportamento já existente de `ReceiveAsync` (que sempre marca `Completed` e remove o `PendingDispatch` depois que `DeliverResponseAsync` retorna, com ou sem falha capturada) passa a se aplicar também a essas duas chamadas, do mesmo jeito que já se aplica a `sender.SendAsync` hoje. Isso cobre falha lógica/determinística (credencial corrompida, chave rotacionada, canal não encontrado); não cobre indisponibilidade de infraestrutura do Postgres que também afete a persistência final desse mesmo bloco de `ReceiveAsync` — gap pré-existente e mais amplo que esta change, documentado em `design.md` (Non-Goals, Risks, Open Questions).
- Nenhuma mudança em `AesGcmChannelCredentialCipher`, em `PushNotificationSender` (fire-and-forget, sem retry, timeout de 5s — `a2a-push-notifications`, Decision 3) ou em rotação de chave.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `inbox-message-history`: o Requirement "Status de entrega da mensagem de saída reflete sucesso ou falha do envio ao provedor" hoje só cobre falha da chamada ao sender (`sender de entrega... lança`). Passa a cobrir também falha ao resolver o canal de origem ou ao decifrar sua credencial — etapas que acontecem antes do sender ser invocado e hoje não têm essa contraparte.

## Impact

- **apps/inbox** (`Buteco.Inbox`): único app afetado. Arquivo de produção: `Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs`. Nenhuma mudança de schema, de contrato HTTP externo, ou de configuração.
- **apps/inbox** (`Buteco.Inbox.Tests`): novo teste em `PushNotificationEndpointsTests.cs`, par direto do já existente `ReceiveAsync_SenderThrows_PersistsOutboundMessageAsFailedWithReasonAndDoesNotFailRequest`, forçando falha de `Decrypt` em vez de falha do sender.
- **Nenhum impacto em apps/api, apps/workers ou apps/frontend.**
- **Documentação**: `02-HISTORICO_E_STATUS.md` (três leituras do timeout do round-trip, causa real, hipótese do sweep descartada, Docker nativo confirmado ausente, gatilho da asserção de convenção 11 pendente) e avaliação de `01-ARQUITETURA_E_CONVENCOES.md` (convenção 4) — tarefas desta change, ver `tasks.md`.
