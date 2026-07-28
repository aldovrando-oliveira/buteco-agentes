## Context

`apps/api` mantém hoje um catálogo de agentes com Create/List/GetById
(`Agents/Commands/CreateAgent`, `Agents/Queries/ListAgents`,
`Agents/Queries/GetAgentById`) e uma rota A2A por agente
(`/agents/{id}/a2a`), resolvida em runtime por `RoutingA2ARequestHandler` →
`AgentA2AServerRegistry` → `A2AServer` (composto manualmente porque o
registro via DI do `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` assume um
único agente por app — ver `openspec/changes/backend-agente-a2a-mvp/design.md`).

Esta mudança adiciona Update e Ativar/Desativar ao CRUD, sem introduzir
exclusão de agente (nem hard nem soft delete). "Inativo" é só mais um valor
de estado do agente — precisa continuar aparecendo em `GET /agents` e
`GET /agents/{id}` (diferente de soft delete, onde o registro
tipicamente desaparece das listagens).

O ponto de maior ambiguidade técnica é o que a rota A2A
(`POST /agents/{id}/a2a`, método `SendMessage`) deve fazer quando o agente
está inativo. Resolvido lendo o código-fonte real do SDK `A2A` (não
decompilado; fonte extraída localmente durante a investigação) e do
`apps/api` atual — ver Decision 2.

## Goals / Non-Goals

**Goals:**
- CRUD completo de agente: Create, Update, Ativar/Desativar, List, GetById.
- `SendMessage` para agente inativo não deve enfileirar trabalho para
  `apps/workers`, mas deve responder de forma protocol-compliant (A2A), sem
  inventar semântica HTTP própria.
- Leituras (`GET /agents`, `GET /agents/{id}`) continuam mostrando agentes
  inativos — quem desativou precisa conseguir encontrar e reativar.

**Non-Goals:**
- Exclusão de agente, hard ou soft (`DeletedAt`) — fora de escopo; o produto
  usa ativar/desativar em vez de deletar.
- Reativação automática ou expiração programada de desativação.
- Edição, ativação ou desativação em lote.
- Concorrência otimista (ETag/If-Match) — último write vence.
- Qualquer mudança em `apps/frontend` — fica para uma próxima change.
- Mudanças em `apps/workers` — o worker já trata falhas de execução
  (`a2a-task-lifecycle`); ele nunca chega a rodar para uma task rejeitada,
  porque o job nunca é publicado.

## Decisions

### Decision 1 — `IsActive` como campo de estado, sem exclusão

`Agent` ganha `public bool IsActive { get; private set; }`, `true` no
construtor. Sem coluna `DeletedAt`, sem `HasQueryFilter` global no
`AppDbContext` — soft delete esconderia o registro das listagens por
padrão, o que contradiz o requisito explícito de que o agente inativo
continue visível em `GET /agents`/`GET /agents/{id}` para poder ser
reativado.

A entidade ganha três métodos mutadores, mantendo a mesma disciplina de
setters privados já usada hoje (construtor é o único ponto de escrita
externo):
- `UpdateDetails(string name, string instructions)` — usado por
  `UpdateAgentCommandHandler`.
- `Activate()` / `Deactivate()` — idempotentes por construção (setar
  `IsActive = true` quando já é `true` é um no-op observável).

Nota sobre idempotência e `UpdatedAt`: "idempotente" aqui se refere ao
resultado observável de `IsActive` (chamar `Activate()` sobre um agente já
ativo, ou `Deactivate()` sobre um já inativo, não é erro e não muda o valor
final), não a ausência total de efeito colateral — `UpdatedAt` é atualizado
em toda chamada, inclusive quando o estado não muda. Isso é aceitável e
consistente com o restante da entidade (qualquer mutação passa por
`UpdatedAt`), mas fica registrado explicitamente para não ser lido como
"chamada repetida é um no-op completo".

Alternativa descartada: soft delete (`DeletedAt` + query filter). Rejeitada
porque o produto já decidiu não ter exclusão nesta fatia — ativo/inativo é
um estado de negócio (o agente existe e continua configurado), não uma
marca de remoção.

### Decision 2 — `SendMessage` para agente inativo rejeita a task via protocolo A2A, sem publicar no RabbitMQ

**Escolha**: em `EnqueueingAgentHandler.ExecuteAsync`, imediatamente após
`updater.SubmitAsync(cancellationToken)` e antes de enfileirar a mensagem no
history / publicar o job:

1. Ler `IsActive` do agente direto do `AppDbContext` (via `IServiceScopeFactory`,
   escopo novo por chamada — mesmo padrão já usado em `PostgresTaskStore` e
   em `AgentA2AServerRegistry.GetOrCreateAsync`).
2. Se `IsActive == false`: chamar `updater.RejectAsync(...)` e retornar,
   **sem** chamar `eventQueue.EnqueueMessageAsync` nem
   `taskJobPublisher.PublishAsync`.
3. Se `IsActive == true`: fluxo atual, inalterado.

Isso usa `TaskState.Rejected`, um estado terminal já modelado pelo
protocolo A2A (`A2A/TaskState.cs`) e exposto via `TaskUpdater.RejectAsync`
— nenhum código HTTP ou de erro novo é inventado.

**Por que a ordem importa (`SubmitAsync` antes de `RejectAsync`, nunca pular
direto para reject)**: verificado no código-fonte do SDK `A2A`
(`TaskProjection.Apply`, chamado por `A2AServer.ApplyEventAsync` a cada
evento emitido pelo handler) que uma `AgentTask` só é persistida no
`ITaskStore` quando o evento aplicado é (a) um evento `Task` completo — o
que `SubmitAsync` emite — ou (b) um `TaskStatusUpdateEvent`/`ArtifactUpdateEvent`
aplicado **sobre uma task já existente no store**. Se o handler emitisse
`RejectAsync` (que só produz um `TaskStatusUpdateEvent`) sem uma
`SubmitAsync` prévia, `TaskProjection.Apply` receberia `current == null` e
devolveria `null` — `ApplyEventAsync` não salvaria nada, e a rejeição
nunca teria sido persistida (o cliente não teria task para consultar depois
via `GetTask`, apesar da resposta síncrona parecer correta).

Como `SubmitAsync` já é chamado incondicionalmente hoje como primeira linha
de `ExecuteAsync`, a task `Submitted` é criada e persistida primeiro; o
`RejectAsync` subsequente aplica a transição sobre essa task já existente e
persiste o estado final. `A2AServer.MaterializeResponseAsync` relê o
`ITaskStore` depois de drenar os eventos, então o `SendMessageResponse`
síncrono devolvido ao cliente HTTP já reflete `Task.Status.State ==
Rejected` — HTTP 200 comum, sem status code especial.

**Alternativas consideradas e descartadas:**

| # | Alternativa | Por que foi descartada |
|---|---|---|
| A | Checar `IsActive` em `AgentA2AServerRegistry.GetOrCreateAsync` (ou no momento de `Register`), recusando ali antes de construir/retornar o `A2AServer` | O `A2AServer` é construído **uma única vez por `agentId`** e fica em cache para sempre num `ConcurrentDictionary` (`AgentA2AServerRegistry.cs`), sem invalidação. Checar `IsActive` nesse ponto só refletiria o estado no instante da primeira chamada — um agente ativado → desativado → reativado depois disso nunca teria o estado reavaliado. É staleness estrutural do cache, não um detalhe de implementação corrigível localmente. |
| B | Lançar `A2AException` em `RoutingA2ARequestHandler.ResolveAsync`, antes de resolver o handler (mesmo padrão hoje usado para "agente não encontrado") | Essa camada resolve **qual** `A2AServer` trata a requisição; não tem acesso a `TaskUpdater`/`AgentEventQueue`, só pode lançar um erro de transporte JSON-RPC. Isso (1) reaproveitaria a mesma forma de erro usada para "agente inexistente", misturando duas semânticas bem diferentes ("não existe" vs. "existe, mas está inativo"), e (2) nunca persistiria uma task — o cliente não teria `taskId` para consultar depois via `GetTask`, ao contrário do que acontece com qualquer outro resultado terminal do protocolo. |
| C | Middleware/filtro HTTP recusando a requisição antes de entrar no pipeline A2A (ex.: HTTP 403) | O protocolo A2A já tem forma própria de resposta (JSON-RPC `SendMessageResponse` ou erro A2A); um código HTTP customizado quebraria a expectativa de qualquer cliente A2A padrão, que só sabe interpretar respostas no formato do protocolo. Contradiz diretamente a decisão de usar o vocabulário nativo do protocolo (`Rejected`) em vez de inventar algo novo. |

### Decision 3 — Estrutura de Commands segue o padrão de `CreateAgent`

`UpdateAgentCommand`, `ActivateAgentCommand`, `DeactivateAgentCommand`
(cada um `ICommand<AgentResponse?>`, retornando `null` quando o `id` não
existe) em `Agents/Commands/{UpdateAgent,ActivateAgent,DeactivateAgent}/`,
mesma forma de `Agents/Commands/CreateAgent/`: handler injeta `AppDbContext`
diretamente (sem repositório intermediário), sem tocar
`IAgentA2AServerRegistry` — nenhum dos três precisa alterar o cache do
registry, porque a Decision 2 já garante que `EnqueueingAgentHandler` lê
`IsActive` fresco a cada `SendMessage`, e não depende do estado do
registry.

`AgentEndpoints.cs` ganha três handlers HTTP no mesmo formato dos
existentes: validam o shape da requisição (`PUT` reaproveita a validação
de nome/instructions de `POST`), montam o Command, chamam
`mediator.Send`, mapeiam `null` para `TypedResults.NotFound()` e o
resultado para `TypedResults.Ok`. Nenhum acesso direto a `AppDbContext` no
endpoint.

## Risks / Trade-offs

- [Risco] Esquecer de ler `IsActive` fresco (ex.: cachear no
  `EnqueueingAgentHandler` construído por agente) reintroduziria a
  staleness da Alternativa A. → Mitigação: a leitura é feita a cada
  `ExecuteAsync`, via escopo novo de `AppDbContext`; coberta por teste de
  integração dedicado (ver `tasks.md`).
- [Risco] `RejectAsync` sem `SubmitAsync` prévio silenciosamente não
  persiste nada (comportamento do SDK `A2A`, não documentado de forma
  óbvia). → Mitigação: a ordem (`SubmitAsync` seguido de `RejectAsync`)
  fica explícita nesta decisão e no código; teste de integração verifica
  que `GetTask` depois do `SendMessage` rejeitado retorna
  `TASK_STATE_REJECTED` (não 404/erro), provando que a persistência
  ocorreu.
- [Trade-off] Ler o banco a cada `SendMessage` (em vez de confiar em algum
  cache) adiciona uma query por chamada. Aceito: é a mesma troca já feita
  em `PostgresTaskStore` e em `AgentA2AServerRegistry.GetOrCreateAsync`
  para existência do agente; consistência do estado de ativação pesa mais
  que essa query extra num caminho que já faz I/O (persistência da task,
  publish no RabbitMQ).

## Migration Plan

- Nova migration EF Core adicionando `is_active boolean not null default true`
  à tabela `agents` — todos os agentes existentes nascem ativos, sem
  necessidade de backfill manual.
- Sem passo de rollback especial: a coluna pode ser removida por uma
  migration reversa padrão; nenhuma outra tabela depende de `IsActive`.
- Sem coordenação com `apps/workers` ou `apps/frontend` — nenhum dos dois é
  tocado nesta change; o frontend simplesmente não expõe os novos endpoints
  ainda.

## Open Questions

(nenhuma — escopo, modelo e a decisão de rejeição via protocolo A2A foram
confirmados lendo o código real antes de propor esta change)
