## Why

`apps/workers` chama o LLM sem nenhum histórico da conversa: `AgentExecutionService`
sempre invoca `aiAgent.RunAsync(userText, session: null, ...)`, e `ExtractLatestUserText`
só lê o `History` da task atual (uma única troca). Um segundo `SendMessage` no mesmo
`contextId` de uma conversa anterior — mesmo com a task anterior `completed` no
mesmo `contextId` — gera uma task nova processada pelo LLM sem nenhum conhecimento
da troca anterior. Isso quebra qualquer cenário de conversa com mais de um turno.

## What Changes

- `apps/workers` passa a montar, antes de cada chamada ao LLM, o histórico da
  conversa do mesmo `contextId`, usando o `AgentSession` nativo do Microsoft
  Agent Framework (`Microsoft.Agents.AI` 1.15.0) em vez de reconstruir a lista
  de mensagens manualmente.
- O estado serializado do `AgentSession` (via `SerializeSessionAsync`/
  `DeserializeSessionAsync`) é persistido dentro de `AgentTask.Metadata` — campo
  de extensão já previsto pelo protocolo A2A — na mesma linha `a2a_tasks` que já
  é gravada ao completar uma task. **Nenhuma tabela ou coluna nova.**
- Limite de histórico via `RecentMessageChatReducer` (`IChatReducer` próprio
  em `apps/workers` — o `MessageCountingChatReducer` nativo do
  `Microsoft.Extensions.AI` é `[Experimental("MEAI001")]`, ver design.md,
  Decisão 4), configurado como constante global (não por agente) — trunca o
  histórico automaticamente antes de cada chamada e o próprio estado
  persistido reflete o corte (não é só um recorte no momento do envio ao
  LLM).
- Tasks que terminam em `failed`/`rejected` não alimentam o histórico: o
  `Metadata` só é atualizado no caminho de sucesso, então a última sessão
  persistida (de uma task `completed` anterior) permanece intacta até o
  próximo sucesso.
- Um lock consultivo do Postgres (`pg_advisory_lock`), escopado a
  `(agentId, contextId)`, serializa o processamento de mensagens do mesmo
  `contextId` entre instâncias concorrentes do worker — sem isso, duas
  instâncias processando o mesmo `contextId` ao mesmo tempo poderiam cada
  uma ler o estado anterior antes da outra gravar o seu, perdendo um turno da
  conversa (lost update).
- `tests/CrossAppTaskStoreCompatibility.Tests` ganha um cenário novo: uma task
  `completed` com `Metadata` populado, escrita pelo `PostgresTaskStore` de
  `apps/api`, lida corretamente pelo de `apps/workers` (e vice-versa) — a
  mesma técnica de verificação cruzada já usada para o resto do schema,
  estendida em vez de duplicada.
- Novo teste de integração em `Buteco.Workers.Tests` (com `IChatClient`
  mockado): a segunda mensagem no mesmo `contextId` chega ao LLM com o
  histórico da primeira incluído.
- **Achado durante teste manual desta change, corrigido aqui por decisão
  explícita** (não é sobre histórico de conversa, mas bloqueava a verificação
  manual dela): `AgentExecutionService` tenta `GetTaskAsync` algumas vezes
  antes de desistir, absorvendo uma corrida pré-existente entre `apps/api`
  publicar no RabbitMQ e as duas escritas separadas com que
  `EnqueueingAgentHandler` grava a task (`SubmitAsync` cria a linha,
  `EnqueueMessageAsync` grava a mensagem do usuário nela depois) — sem esse
  retry (e sem exigir que a mensagem do usuário já esteja presente, não só a
  task), o worker podia consumir a mensagem antes de qualquer uma dessas
  escritas completar e descartá-la silenciosamente, presa em `submitted`
  para sempre. Ver design.md, Decisão 9.
- **Terceiro achado durante teste manual, corrigido aqui por decisão
  explícita**: a sessão serializada é reencodada como uma string JSON
  escapada (`ConversationSessionCodec`, novo) antes de ser guardada em
  `Metadata` — guardar o `JsonElement` diretamente quebrava a
  desserialização na segunda mensagem de qualquer conversa, porque
  `Microsoft.Extensions.AI.AIContent` exige seu discriminador `"$type"`
  como primeira propriedade do objeto, e o `jsonb` do Postgres não preserva
  ordem de propriedades. Ver design.md, Decisão 10.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `a2a-task-lifecycle`: o requirement "Workers processam a task até um estado
  terminal" passa a exigir que a chamada ao LLM inclua o histórico da
  conversa do mesmo `contextId`, com limite de tamanho e exclusão de
  turnos `failed`/`rejected`.

## Impact

- **apps/workers** (`Buteco.Workers`): `AgentExecutionService` (monta o
  `AgentSession`, injeta o reducer, persiste/recupera via `Metadata`, adquire
  o advisory lock, retry curto em `GetTaskAsync` — Decisão 9);
  `ConversationSessionCodec` novo (Decisão 10); nenhuma mudança de
  schema/migration.
- **apps/workers** (`Buteco.Workers.Tests`): novos testes de integração de
  histórico multi-turno e de retry em `GetTaskAsync`, e teste unitário
  (sem Postgres) do `ConversationSessionCodec`.
- **tests/CrossAppTaskStoreCompatibility.Tests**: novo cenário cobrindo
  `Metadata` em tasks `completed`, usando o mesmo `ConversationSessionCodec`
  de `apps/workers` para refletir o formato real persistido.
- **apps/api**: nenhuma mudança de comportamento (non-goal explícito) — o
  agrupamento por `contextId` no protocolo A2A já funciona; `apps/api` nunca
  lê ou escreve `Metadata["conversationSession"]`, então tasks escritas antes
  desta mudança (sem esse campo) são tratadas como "sem sessão anterior" de
  forma segura. Única exceção, explicitamente pedida: um comentário de
  documentação (sem mudança de comportamento) na cópia de `ListTasksAsync` de
  `apps/api`, apontando a mesma lacuna de filtro por `AgentId` corrigida na
  cópia de `apps/workers` — ver Decisão 2 do design.md.
- **apps/frontend**: nenhuma mudança.
