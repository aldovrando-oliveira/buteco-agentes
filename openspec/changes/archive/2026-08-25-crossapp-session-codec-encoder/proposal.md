## Why

`tests/CrossAppTaskStoreCompatibility.Tests` está 0/2 desde 2026-08-01
(commit `1470b72`), de forma determinística. A causa raiz, confirmada por
execução real contra Postgres, `git bisect` em worktrees isolados e
decompilação de `A2A.dll`, é um site de produção fora do contrato de
serialização A2A: `ConversationSessionCodec.Encode`
(`apps/workers/src/Buteco.Workers/Agents/ConversationSessionCodec.cs:24-25`)
chama `JsonSerializer.SerializeToElement(text)` sem passar
`A2AJsonUtilities.DefaultOptions`, cujo `Encoder` é
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (confirmado por
decompilação). O resto do pipeline A2A — os dois `PostgresTaskStore`, o
`PushNotificationSender` — usa essas mesmas opções consistentemente.

O teste em si está correto: ele compara texto bruto (`GetRawText()`) do
valor de `Metadata["conversationSession"]` construído diretamente via
`ConversationSessionCodec.Encode` (usando o encoder padrão do .NET, que
escapa aspas como `"`) contra o valor que sai do round-trip real de
persistência (que usa o encoder relaxado do A2A, `\"`). Essa comparação
falharia mesmo com um único app conversando consigo mesmo — não é uma
divergência entre `apps/api` e `apps/workers`, é uma inconsistência interna
de `JsonSerializerOptions` num único método. Não há dado em risco: o
`Deserialize` sempre teve sucesso, e nenhuma linha em `a2a_tasks` fica
ilegível — as duas formas de escape decodificam para a mesma string.

Corrigir isso importa porque este teste é o único do repo que grava com o
`PostgresTaskStore` de um app e lê com o do outro contra o mesmo Postgres
— o mecanismo mais forte de verificação de acordo de schema entre
`apps/api` e `apps/workers` (dois `AppDbContext` sincronizados por
disciplina, sem schema separado). Ele está desligado, catalogado como
"falha conhecida", e por isso mudo: as outras seis asserções da mesma
função (`Id`, `ContextId`, `Status.State`, contagens e conteúdo de
`History`/`Artifacts`) continuam passando a cada execução, mas ninguém
percebe — teste vermelho não distingue asserção nova de regressão real.

## What Changes

- Corrige `ConversationSessionCodec.Encode` para serializar com
  `A2AJsonUtilities.DefaultOptions`, alinhando-o ao resto do pipeline A2A
  (`PostgresTaskStore` dos dois apps, `PushNotificationSender`).
- Varredura completa de todo site de produção e teste em `apps/api`,
  `apps/workers` e `tests/` (raiz) que chama `JsonSerializer.Serialize`/
  `Deserialize`/`SerializeToElement` sobre um tipo ou payload A2A,
  classificando cada um (correto hoje, fora do contrato mas não
  aplicável, ou fora do contrato e corrigido nesta change).
- Adiciona um comentário na asserção estrita de `AssertTasksMatch`
  (`tests/CrossAppTaskStoreCompatibility.Tests/PostgresTaskStoreCompatibilityTests.cs:158-160`)
  registrando que a comparação textual é o ponto — não relaxar para
  comparação semântica no futuro achando que é conserto de teste.
- Formaliza como requisito testável, em `a2a-task-lifecycle`, que valores
  colocados em `AgentTask.Metadata` por um app são lidos de volta
  byte-identicamente por `PostgresTaskStore` do outro app.
- Registra o diagnóstico, o bisect e a janela de 24 dias/11 commits sem
  sinal em `02-HISTORICO_E_STATUS.md`, e acrescenta um terceiro exemplo à
  convenção 12 em `01-ARQUITETURA_E_CONVENCOES.md` (D5 do `design.md`:
  avaliado, cabe).

Fora de escopo: `PushNotificationConfigCodec.Encode`
(`apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs:16-17`)
tem o mesmo defeito de forma, mas com efeito mais sério — casing errado
(`Url`/`Token` em vez de `url`/`token`) e nulls explícitos gravados em
`Metadata["pushNotificationConfig"]` desde 2026-08-08. Confirmado, não
potencial: exposto a qualquer cliente do protocolo A2A que chame
`GetTask`/`ListTasks` (`GET /agents/{id}/a2a`,
`RoutingA2ARequestHandler.cs:47-56`) — a `AgentTask` inteira, `Metadata`
incluída, volta sem filtragem, e nenhum código do repo relê essa chave
para reformatá-la. Fica para uma change própria subsequente (decisão já
tomada, não uma pergunta em aberto), mas a confirmação da exposição
eleva a prioridade dessa change subsequente — ver design.md.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `a2a-task-lifecycle`: a requirement "Workers processam a task até um
  estado terminal" ganha um novo cenário — o resultado escrito no store
  durável compartilhado deve ser legível de forma byte-idêntica pelo
  `PostgresTaskStore` de `apps/api`, e não só pelo de `apps/workers` que o
  escreveu.

## Impact

- `apps/workers/src/Buteco.Workers/Agents/ConversationSessionCodec.cs` —
  correção do encoder.
- `tests/CrossAppTaskStoreCompatibility.Tests/PostgresTaskStoreCompatibilityTests.cs`
  — só comentário; a asserção em si não muda (Decisão D2 do design.md).
  Ambos os testes passam a ficar verdes.
- `openspec/specs/a2a-task-lifecycle/spec.md` — novo cenário na
  requirement existente.
- `02-HISTORICO_E_STATUS.md` — atualiza a entrada de baseline, registra
  diagnóstico/bisect/janela sem sinal.
- `01-ARQUITETURA_E_CONVENCOES.md` — acrescenta o terceiro exemplo à
  convenção 12 (D5 do design.md).
- Nenhuma migração de dados em `a2a_tasks` (D3 do design.md).
- Nenhuma alteração esperada em `ConversationSessionCodecTests` (D4: fora
  do contrato A2A, serializa `JsonObject`/`JsonNode` de apoio, não
  `AgentTask`) — se a Tarefa 5.1 acusar quebra, é achado a reportar, não
  ajuste de expectativa.
- Nenhuma mudança de código em `apps/api`; o SHALL ampliado (D6) descreve
  comportamento que já existe lá, não comportamento novo.
