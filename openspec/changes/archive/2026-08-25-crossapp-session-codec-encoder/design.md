## Context

`ConversationSessionCodec` (`apps/workers/src/Buteco.Workers/Agents/ConversationSessionCodec.cs`)
existe desde a change `apps-workers-historico-conversa` (Decisão 10):
codifica o `AgentSession` serializado como uma string JSON escapada
(escalar, opaca para o `jsonb` do Postgres) antes de gravá-lo em
`AgentTask.Metadata["conversationSession"]`, para sobreviver à
normalização de ordem de propriedades que o `jsonb` faz.

O resto do pipeline A2A — os dois `PostgresTaskStore` (`apps/api` e
`apps/workers`) e `PushNotificationSender` — serializa/desserializa
`AgentTask` inteiro com `A2AJsonUtilities.DefaultOptions`. Confirmado por
decompilação (`strings` no `A2A.dll` 1.0.0-preview2): essas opções
configuram `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
(produz `\"` para aspas embutidas) e uma política de nomes que serializa
tipos A2A em `camelCase` sem campos nulos.

`ConversationSessionCodec.Encode` chama
`JsonSerializer.SerializeToElement(text)` sem passar essas opções — cai
no `JsonSerializerOptions` padrão do .NET, cujo encoder escapa aspas como
`"`. Isso não corrompe nada (o valor é reencodado no round-trip real
de `Serialize(task, A2AJsonUtilities.DefaultOptions)` de qualquer store
que grave a task), mas produz um valor "esperado" divergente do "actual"
no teste de compatibilidade cross-app, que compara `GetRawText()`
literalmente — e essa comparação estrita é o ponto do teste, não um
defeito dele (convenção 11).

Verificado por bisect real em worktrees (`7a56376` passa 2/2; `1470b72`,
que introduziu o cenário, já falha 0/2; `HEAD` falha idêntico): a falha
nasceu no mesmo commit que introduziu a asserção, não é regressão de
dependência (única versão do pacote `A2A`, `1.0.0-preview2`, pinada em
`Directory.Packages.props` para os dois apps).

## Goals / Non-Goals

**Goals:**
- Fazer `ConversationSessionCodec.Encode` serializar com o mesmo
  `JsonSerializerOptions` do resto do pipeline A2A.
- Confirmar, com uma varredura escrita, que nenhum outro site de produção
  do payload A2A persistido em `a2a_tasks` está fora desse contrato.
- Formalizar o invariante "metadata escrito por um app é lido
  byte-identicamente pelo outro" como requisito testável.
- Registrar o diagnóstico e a janela sem sinal na documentação viva do
  repo.

**Non-Goals:**
- Corrigir `PushNotificationConfigCodec.Encode` — mesmo defeito de forma
  (falta de `A2AJsonUtilities.DefaultOptions`), mas com efeito mais sério
  (casing `Url`/`Token` em vez de `url`/`token`, nulls explícitos gravados
  em produção desde 2026-08-08) e **confirmado exposto**, não
  potencialmente: qualquer cliente do protocolo A2A que chame
  `GetTask`/`ListTasks` (`GET /agents/{id}/a2a`,
  `RoutingA2ARequestHandler.cs:47-56`) recebe a `AgentTask` inteira,
  `Metadata` incluída, sem filtragem — e nenhum código do repo relê
  essa chave para reformatá-la. Um teste
  (`PushNotificationEndToEndTests.cs:67`) precisa mudar de expectativa
  junto. Decisão já tomada de tratar como change própria subsequente,
  para não misturar um bug de escaping benigno com um bug de contrato de
  wire format com exposição externa confirmada (ver Risco correspondente
  — a confirmação eleva a prioridade dessa change subsequente, não o
  escopo desta).
- Relaxar ou trocar a asserção de `AssertTasksMatch` por comparação
  semântica (Decisão D2).
- Migração ou reescrita de dados em `a2a_tasks` (Decisão D3).
- Qualquer mudança na Decisão 10 (escalar opaco para sobreviver à
  reordenação do `jsonb`) — continua válida, resolve um problema
  diferente (ordem de propriedades, não encoding de aspas).

## Decisions

### D1 — Corrigir a origem (`ConversationSessionCodec.Encode`), não a asserção do teste

```diff
-    public static JsonElement Encode(JsonElement serializedSession) =>
-        JsonSerializer.SerializeToElement(serializedSession.GetRawText());
+    public static JsonElement Encode(JsonElement serializedSession) =>
+        JsonSerializer.SerializeToElement(serializedSession.GetRawText(), A2AJsonUtilities.DefaultOptions);
```

**Alternativa rejeitada**: trocar a asserção de `AssertTasksMatch` (linha
158-160) para comparar valor semântico (ex.: `JsonNode.DeepEquals`, ou
decodificar ambos os lados e comparar strings decodificadas) em vez de
`GetRawText()`. Faria o teste passar imediatamente, mas mataria
permanentemente a capacidade dessa asserção de detectar divergência real
de encoding entre os dois `PostgresTaskStore` — que é exatamente o que
ela existe para guardar (convenção 11: teste de acordo entre dois lados
usa o artefato real produzido por um contra o outro). Corrigir a origem
faz os dois lados produzirem o mesmo texto de verdade, então a asserção
estrita passa a valer e continua guardando. É também mais estável a
mudanças futuras do pacote A2A: se o encoder padrão do A2A mudar, os dois
lados do teste mudam juntos, porque os dois passam pela mesma
`A2AJsonUtilities.DefaultOptions`.

### D2 — `AssertTasksMatch` permanece com comparação textual estrita

Nenhuma mudança na asserção em si. Ganha só um comentário (ver Risco 3)
apontando que a igualdade textual é deliberada.

### D3 — Nenhuma migração de dados em `a2a_tasks`

Confirmado experimentalmente **antes** da correção: rodando
`PostgresTaskStoreCompatibilityTests` contra o commit `f668be5` (HEAD
pré-`apply`, código antigo, sem D1), o valor "actual" — a linha gravada
pelo `ConversationSessionCodec.Encode` de **hoje** (sem
`A2AJsonUtilities.DefaultOptions`) e relida do Postgres — já saía como
`\"` (encoder relaxado), nas duas direções do teste. Ou seja: uma linha
gravada pelo código atual, quando persistida e relida por qualquer
`PostgresTaskStore`, já está no formato relaxado hoje — o bug está
isolado ao valor em memória que `Encode` retorna antes de qualquer
persistência (o encoder padrão do .NET, usado só ali), não ao que chega
no disco. Isso porque o valor retornado por `Encode` nunca é gravado
bruto: ele entra em `AgentTask.Metadata`, e a task inteira é reserializada
por `Serialize(task, A2AJsonUtilities.DefaultOptions)` dentro de
`SaveTaskAsync`, que reescreve o encoding antes de tocar o banco.

Essa é a evidência que sustenta D3, capturada no commit pré-`apply`
precisamente porque é o único lugar onde "linha gravada pelo código
antigo, relida" pode existir de verdade — um Postgres novo do
Testcontainers rodado depois da correção só teria linhas escritas pelo
código já corrigido, e não provaria nada sobre o formato anterior. Nenhuma
linha existente em `a2a_tasks` muda de conteúdo com esta correção: a
correção move **onde** o encoder relaxado é aplicado (de dentro de
`SaveTaskAsync`, tarde, para dentro de `Encode`, cedo), não **o que**
fica gravado.

### D4 — Varredura de sites de serialização A2A (classificação completa)

Todo `JsonSerializer.Serialize`/`Deserialize`/`SerializeToElement` em
`apps/api`, `apps/workers` (produção e testes) e `tests/` (raiz):

| Site | Usa `A2AJsonUtilities.DefaultOptions`? | Classificação |
|---|---|---|
| `apps/api/.../A2A/PostgresTaskStore.cs:93,96` | Sim | Correto — payload A2A persistido. |
| `apps/workers/.../A2A/PostgresTaskStore.cs:98,101` | Sim | Correto — payload A2A persistido. |
| `apps/workers/.../Notifications/PushNotificationSender.cs:26` | Sim | Correto — payload enviado a webhook externo. |
| `apps/workers/.../Agents/ConversationSessionCodec.cs:25` | **Não** | **Bug — corrigido nesta change (D1).** |
| `apps/workers/.../Agents/PushNotificationConfigCodec.cs:17` | **Não** | **Bug real, mais sério (casing + nulls). Fora de escopo, change própria subsequente — ver Non-Goals.** |
| `apps/workers/.../AgentDelegations/DelegationDepth.cs:29` | Não | Seguro apesar de fora do padrão: serializa um `int` puro — sem strings, sem nomes de propriedade, encoder/naming policy são irrelevantes para um literal numérico. |
| `apps/api/.../Messaging/RabbitMqTaskJobPublisher.cs:25` + `apps/workers/.../Messaging/TaskJobConsumer.cs:49` (e a cópia simétrica de `apps/workers/.../Messaging/RabbitMqTaskJobPublisher.cs:30`) | Não (nenhum dos dois lados) | Seguro: `TaskJobMessage` é um envelope interno de fila (RabbitMQ), com definição própria e simétrica em cada app — não é o payload A2A persistido/exposto em `a2a_tasks` nem no protocolo A2A. Escreve e lê com o mesmo `JsonSerializerOptions` padrão dos dois lados, então é internamente consistente mesmo fora do contrato A2A. Contém um campo `PushNotificationConfig`, mas só como carga transportada — não é reencodado em `Metadata` por este caminho. |
| `apps/api/.../Auth/TokenService.cs:17,54` | N/A | Fora de escopo — `TokenPayload` não é tipo/payload A2A. |
| `apps/api/.../Infrastructure/AppDbContext.cs:53-54,110-111` + cópia em `apps/workers/.../Infrastructure/AppDbContext.cs:92-93` | N/A | Fora de escopo — conversão de `Skill`/`AllowedTools` (domínio próprio, jsonb), não payload A2A. |
| `apps/*/tests/**` (`ConversationHistoryTests`, `AgentDelegation*Tests`, `TaskJobConsumerTests`, `PushNotificationEndToEndTests`, `McpToolExecutionEndToEndTests`) | Sim, em todo `Serialize`/`Deserialize` de `AgentTask` | Correto — os fixtures de teste já seguem o padrão certo ao montar payload A2A. |
| `ConversationSessionCodecTests.cs:60,93` | N/A (serializa `JsonObject`/`JsonNode` de apoio ao teste, não `AgentTask`) | Fora de escopo — utilitário de reordenação de propriedades para simular o `jsonb`, não payload A2A real. |
| `*/Support/FakeMcpServerHttpMessageHandler.cs` (2 cópias) | N/A | Fora de escopo — resposta MCP simulada, protocolo diferente. |

Nenhum site além dos dois já conhecidos (`ConversationSessionCodec`,
`PushNotificationConfigCodec`) está fora do contrato A2A. Resultado
registrado por completo, inclusive os sites seguros, conforme pedido.

### D5 — Convenção 12 ganha um terceiro exemplo

Avaliado: cabe. Os dois exemplos atuais de "contrato entre apps inclui o
formato de fio" (`01-ARQUITETURA_E_CONVENCOES.md`, convenção 12) são sobre
representação de enum (string vs. ordinal). Este achado é a mesma
convenção numa dimensão diferente — opções de serialização (encoder de
escaping, naming policy, tratamento de null) são parte do formato de fio
tanto quanto a representação de um campo específico, porque dois sites
que serializam o mesmo tipo com opções diferentes produzem payloads
estruturalmente diferentes mesmo com os campos "certos". `tasks.md` inclui
a edição em `01-ARQUITETURA_E_CONVENCOES.md` acrescentando este terceiro
exemplo.

### D6 — Cenário novo permanece em `a2a-task-lifecycle`, com o SHALL ampliado

Avaliado: mover o cenário para uma capability própria de compatibilidade
cross-app exigiria criar uma capability nova (`proposal.md` ganharia uma
entrada em "New Capabilities", hoje vazia) só para um único cenário.
Mais simples e não menos correto: o SHALL da requirement "Workers
processam a task até um estado terminal" passa a mencionar explicitamente
a legibilidade cruzada pelo `PostgresTaskStore` de `apps/api`, fechando a
lacuna entre o texto normativo (que falava só do que `apps/workers` faz) e
o `THEN` do cenário (que afirma o que `apps/api` retorna). Quem lê só o
requisito agora sabe que isso é contrato, não um detalhe implícito do
cenário.

Consequência registrada, não bloqueante: o SHALL de `a2a-task-lifecycle`
fica maior e passa a misturar ciclo de vida de task com contrato de
persistência cross-app. **Gatilho natural para revisitar**: se aparecer
um segundo cenário de compatibilidade entre os dois `PostgresTaskStore`
(além deste), a capability própria passa a se pagar — cindir nessa
ocasião, não antes.

## Risks / Trade-offs

- **[Risco] A correção muda o que é gravado em `a2a_tasks` para tasks
  futuras** → **Mitigação**: confirmado empiricamente (D3) que o formato
  já gravado hoje, antes da correção, é o mesmo que a correção produz — a
  correção move onde o encoder relaxado é aplicado, não o resultado final
  gravado. Este risco não é coberto pelo teste cross-app: esse teste é
  simétrico por construção (passa igual com `\"` ou `"` no disco,
  desde que os dois lados concordem entre si) e por isso não prova nada
  sobre o formato ter mudado em relação a antes — só sobre os dois lados
  concordarem hoje. A evidência real está em D3, capturada contra o
  commit pré-`apply`.
- **[Risco] Os dois `PostgresTaskStore` divergirem de novo no futuro (ex.:
  outro codec introduzido fora do contrato, ou um dos dois apps trocando
  de opções isoladamente)** → **Mitigação**: este é o risco que o cenário
  novo do spec e `PostgresTaskStoreCompatibilityTests` (corrigido por
  esta change) de fato cobrem — verificam ativamente, a cada execução,
  que os dois `PostgresTaskStore` concordam byte a byte sobre o mesmo
  valor de `Metadata` (convenção 11: teste de acordo entre dois lados
  usando o artefato real de um contra o outro).
- **[Risco] Outro site de serialização A2A fora do contrato além dos dois
  já conhecidos** → **Mitigação**: varredura completa em D4, com
  classificação escrita de cada site encontrado, incluindo os seguros.
  Resultado: nenhum site adicional.
- **[Risco] A asserção estrita de `AssertTasksMatch` ser relaxada no
  futuro por alguém "consertando o teste" sem reconhecer que a
  comparação textual é o ponto** → **Mitigação**: comentário adicionado
  na própria asserção (linha 158-160), no mesmo espírito da asserção
  negativa da convenção 13 — explicando que a igualdade é sobre o texto
  bruto de propósito, com ponteiro para esta change.
- **[Risco] `PushNotificationConfigCodec` continuar gravando payload fora
  do contrato em produção enquanto a change própria não é proposta** →
  **Confirmado, não potencial**: verificado por leitura de
  `RoutingA2ARequestHandler.GetTaskAsync`/`ListTasksAsync`
  (`apps/api/src/Buteco.Api/A2A/RoutingA2ARequestHandler.cs:47-56`,
  mapeado em `Program.cs:78` como `GET /agents/{id}/a2a`) — a `AgentTask`
  inteira, `Metadata` incluída, volta sem filtragem a qualquer cliente do
  protocolo A2A que chame `GetTask`/`ListTasks`; nenhum código do repo lê
  `Metadata["pushNotificationConfig"]` de volta para reformatá-la, então
  essa é a única leitura real hoje. Não mitigado nesta change (fora de
  escopo, decisão explícita — ver Non-Goals). **Gatilho para revisitar**:
  antes de qualquer mudança em `apps/workers/.../Notifications/` ou em
  `PushNotificationConfigCodec`; e, dado que o endpoint já expõe o
  formato errado desde 2026-08-08, a change própria (Non-Goal desta)
  deixa de ser "sucessora eventual" e passa a ser prioridade de
  sequenciamento — decisão de escopo/prioridade para quem revisar,
  registrada aqui e em `02-HISTORICO_E_STATUS.md` para não depender só da
  memória desta sessão.

## Migration Plan

Sem passos de deploy ou rollback além do deploy normal de `apps/workers`
— é uma correção de biblioteca interna sem mudança de schema, sem
migração de banco, sem coordenação com `apps/api`. Rollback é reverter o
commit; nenhuma linha de `a2a_tasks` fica em formato incompatível com a
versão anterior do código (D3).

## Open Questions

Nenhuma incerteza real de negócio/produto em aberto. O escopo do
`PushNotificationConfigCodec` já foi decidido (change própria
subsequente, não uma pergunta) e está registrado nos Non-Goals.
