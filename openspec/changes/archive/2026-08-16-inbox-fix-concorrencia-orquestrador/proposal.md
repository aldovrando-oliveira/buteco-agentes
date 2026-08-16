## Why

`InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
falha de forma determinística (confirmado em 5 execuções isoladas
consecutivas): `Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException`
em `InboundMessageOrchestrator.cs:50`. Isso viola o Requirement já
existente "Debounce agrupa mensagens dentro da janela configurada" da
capability `inbox-message-orchestration` — sob concorrência real (várias
mensagens quase simultâneas da mesma Session, o caso comum de um usuário
digitando várias mensagens seguidas em um canal de chat), o orquestrador
pode falhar em persistir a mensagem em vez de agrupá-la ao buffer
existente.

## What Changes

- Corrige `InboundMessageOrchestrator.ReceiveMessageAsync`: o caminho de
  append a uma `PendingDispatch` `Pending` já existente (tanto o caminho
  direto quanto o caminho de retry após colidir com a violação de
  unicidade na criação) hoje faz um read-modify-write único, sem retry,
  contra uma linha protegida por concorrência otimista via `xmin`. Sob
  múltiplas chamadas concorrentes para a mesma `Session`, apenas uma
  consegue vencer o `SaveChangesAsync`; as demais recebem
  `DbUpdateConcurrencyException` não tratada e a mensagem do usuário é
  perdida (a chamada falha) em vez de ser bufferizada.
- Adiciona um loop de retry limitado que, ao capturar
  `DbUpdateConcurrencyException` no append, recarrega o estado atual da
  linha, reaplica `AppendMessage` e tenta `SaveChangesAsync` novamente
  até suceder ou esgotar o limite de tentativas.
- Nenhuma mudança de comportamento especificado: o Requirement "Debounce
  agrupa mensagens dentro da janela configurada" já exige que mensagens
  concorrentes da mesma Session colapsem num único buffer — este é um
  fix de bug dentro do que já está especificado, não uma capability nova.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

(nenhuma — o Requirement relevante em `inbox-message-orchestration`
("Debounce agrupa mensagens dentro da janela configurada") já cobre o
comportamento correto; este change corrige a implementação para
cumpri-lo sob concorrência real, sem alterar o texto do Requirement ou
seus Scenarios.)

**Nota sobre `openspec validate`**: seguindo o precedente de
`apps-api-cqrs-mediator` (`openspec/changes/archive/2026-07-26-apps-api-cqrs-mediator/proposal.md`),
esta change mantém `specs/` intencionalmente vazio — é um fix de bug
interno sem nenhum requisito ou cenário observável novo, alterado ou
removido. O schema `spec-driven` desta instalação do CLI exige
mecanicamente ao menos um delta em `specs/` para `openspec validate`
passar; esse erro é um falso positivo conhecido para changes que
genuinamente não alteram nenhum requisito. `/opsx:apply` funciona
normalmente (`tasks.md` é o único artefato exigido para implementar).

## Impact

- `apps/inbox/src/Buteco.Inbox/Orchestration/InboundMessageOrchestrator.cs`
  — único arquivo de produção afetado.
- Nenhuma migration nova, nenhuma mudança de schema: o mecanismo de
  concorrência otimista (`xmin`) já existe na tabela `pending_dispatches`
  desde `inbox-orquestrador-debounce`.
- Nenhum impacto em `apps/api`, `apps/workers` ou `apps/frontend` — fix
  interno a `apps/inbox`.
