## Context

`PushNotificationConfigCodec` (`apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs`)
existe desde `a2a-push-notifications`: codifica o `PushNotificationConfig`
recebido no `SendMessage` para gravação em
`AgentTask.Metadata["pushNotificationConfig"]`, no mesmo padrão de
`DelegationDepth`/`ConversationSessionCodec` (design.md daquela change,
Decision 1).

`Encode` chama `JsonSerializer.SerializeToElement(config)` sem passar
`A2AJsonUtilities.DefaultOptions` — o mesmo defeito de forma que
`ConversationSessionCodec` tinha antes de `crossapp-session-codec-encoder`.
Esse defeito foi encontrado pela varredura daquela change e
deliberadamente deixado fora do escopo dela, registrado como item em
aberto em `02-HISTORICO_E_STATUS.md`. Esta change fecha esse item.

Uma rodada de `/opsx:explore` fechou as sete perguntas em aberto listadas
naquele item, por leitura de código, decompilação do `A2A.dll`
(`ilspycmd`), reprodução do path de produção real num projeto .NET
descartável, query direta no Postgres de dev e consulta à spec A2A na
fonte (`a2a-protocol.org/latest/specification/`). Os achados abaixo são
o resultado, não hipótese.

**Por que este caso é mais sério que `ConversationSessionCodec`**: lá o
`Encode` produz uma string escalar (`JsonElement.Kind == String`); aqui
`Encode` produz um objeto (`JsonElement.Kind == Object`), e a diferença
importa porque `PostgresTaskStore.SaveTaskAsync` reserializa o `AgentTask`
inteiro com `Serialize(task, A2AJsonUtilities.DefaultOptions)` antes de
gravar. Reproduzindo esse path exatamente (config só com `Url`, contra o
`A2A.dll` real):

```
Encode() isolado:
  {"Id":null,"Url":"https://cliente.example.com/webhooks/a2a","Authentication":null,"Token":null}

Payload gravado em a2a_tasks.payload após SaveTaskAsync:
  {"id":"task-1","contextId":"ctx-1","status":{...},
   "metadata":{"pushNotificationConfig":
     {"Id":null,"Url":"https://cliente.example.com/webhooks/a2a","Authentication":null,"Token":null}}}
```

O envelope externo (`id`, `contextId`, `status`) sai corretamente em
camelCase — são objetos .NET serializados a fresco pela re-serialização.
O subtree `metadata.pushNotificationConfig` sobrevive **verbatim**,
porque é um `JsonElement` já materializado: `JsonSerializer.Serialize`
escreve o `JsonElement` copiando a árvore JSON já parseada, sem
reaplicar naming policy — naming policy só atua serializando um objeto
.NET a partir de reflection/metadata de tipo. É por isso que este defeito
de forma, ao contrário do de `ConversationSessionCodec`, chega ao disco:
não há uma segunda passada de serialização "a fresco" entre o `Encode`
buggy e o Postgres.

Diff completo, com todos os campos de `PushNotificationConfig` (não só
`Url`/`Token` — `Authentication` tem o mesmo defeito recursivamente em
`Scheme`/`Credentials`):

| Config | Hoje (`Encode` sem options) | Com `A2AJsonUtilities.DefaultOptions` |
|---|---|---|
| só `Url` | `{"Id":null,"Url":"...","Authentication":null,"Token":null}` | `{"url":"..."}` |
| `Url`+`Token` | `{"Id":null,"Url":"...","Authentication":null,"Token":"tok-123"}` | `{"url":"...","token":"tok-123"}` |
| `Url`+`Authentication` | `..."Authentication":{"Scheme":"Bearer","Credentials":"cred"},...` | `..."authentication":{"scheme":"Bearer","credentials":"cred"},...` |

Confirmado contra a spec A2A (`a2a-protocol.org/latest/specification/`):
os campos de `PushNotificationConfig` são `url` (obrigatório), `token`,
`authentication`, `id` — camelCase, opcionais omitidos e não `null`.
Bate exatamente com `A2AJsonUtilities.DefaultOptions`
(`JsonSerializerDefaults.Web`: `PropertyNamingPolicy = CamelCase`,
`PropertyNameCaseInsensitive = true`; `DefaultIgnoreCondition =
WhenWritingNull` fixado explicitamente pela A2A SDK). Verificado por
reflection real, não assumido — `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
tem `PropertyNameCaseInsensitive == true`.

Levantamento no Postgres de dev local (`buteco_agents`, **não** produção
— não há acesso a produção neste ambiente):

```sql
SELECT count(*) AS total_tasks,
       count(*) FILTER (WHERE payload::jsonb -> 'metadata' ? 'pushNotificationConfig') AS with_push_config,
       count(*) FILTER (WHERE payload::jsonb #> '{metadata,pushNotificationConfig}' ? 'Url') AS with_buggy_casing,
       count(*) FILTER (WHERE payload::jsonb #> '{metadata,pushNotificationConfig}' ? 'url') AS with_correct_casing
FROM a2a_tasks;

 total_tasks | with_push_config | with_buggy_casing | with_correct_casing
-------------+------------------+--------------------+---------------------
         175 |               37 |                37  |                   0
```

100% das linhas com a chave estão no formato errado — consistente com
"sem correção desde 2026-08-08".

**Não executado contra o Postgres de produção**: sem acesso a esse
ambiente nesta sessão de apply. Os números de produção seguem
desconhecidos — quem tiver acesso deve rodar a query acima lá.

## Goals / Non-Goals

**Goals:**
- Fazer `PushNotificationConfigCodec.Encode` serializar com o mesmo
  `JsonSerializerOptions` do resto do pipeline A2A.
- Corrigir a expectativa do teste E2E que hoje pina o formato errado,
  nas duas ocorrências, e estender a cobertura aos campos aninhados
  (`token`, `authentication.scheme`/`authentication.credentials`) nos
  testes que já variam esses campos — o cenário de spec novo afirma o
  formato dos quatro campos de `PushNotificationConfig`, não só `url`.
- Formalizar o formato de fio de `Metadata["pushNotificationConfig"]`
  como cenário testável em `a2a-push-notifications`.
- Fechar o item em aberto registrado em `02-HISTORICO_E_STATUS.md` por
  `crossapp-session-codec-encoder`.

**Non-Goals:**
- Migração ou backfill das linhas já gravadas em `a2a_tasks` (Decisão D4
  — decisão explícita, com gatilho, não ausência de trabalho).
- Qualquer mudança no comportamento de push notification: retry, timeout
  de 5s, fire-and-forget (`a2a-push-notifications`, Decision 3) seguem
  como estão.
- Adicionar `Decode` a `PushNotificationConfigCodec` — não existe hoje e
  nenhum consumidor encontrado no código do repo precisa dele (Decisão
  D2). Se um consumidor real aparecer, essa é uma decisão de escopo
  separada.
- Os demais grupos da baseline nomeada de testes (isolamento de fixture
  em `apps/inbox`, timeout do podman em `InboxOrchestratorRoundTrip`, os
  testes intermitentes do frontend) e `inbox-contexto-canal-metadata`
  (etapa 2 da linha de contexto temporal) — não relacionados.

## Decisions

### D1 — Corrigir a origem (`PushNotificationConfigCodec.Encode`), não a asserção do teste

```diff
-    public static JsonElement Encode(PushNotificationConfig config) =>
-        JsonSerializer.SerializeToElement(config);
+    public static JsonElement Encode(PushNotificationConfig config) =>
+        JsonSerializer.SerializeToElement(config, A2AJsonUtilities.DefaultOptions);
```

Convenção 12 (`01-ARQUITETURA_E_CONVENCOES.md`): defeito de formato de
fio pertence a quem serializa, corrigido lá — não contornado no
consumidor. Já tem um terceiro exemplo registrado por
`crossapp-session-codec-encoder`; este é mais um caso da mesma
convenção, não precisa de exemplo novo no documento (ver D6 sobre o que
de fato vale registrar ali).

**Alternativa rejeitada**: normalizar o valor no momento da leitura (em
`RoutingA2ARequestHandler` ou num novo `Decode`), deixando `Encode` como
está. Rejeitada porque trataria o sintoma no ponto de exposição em vez
da causa, deixaria o dado gravado permanentemente incorreto (violando o
próprio propósito de persistir um valor conforme à spec), e exigiria
introduzir um `Decode` que hoje não existe e que nenhum consumidor
requisita — trabalho a mais para esconder, não resolver, o defeito.

### D2 — Sem `Decode` nem migração de leitura interna

Confirmado por leitura: `PushNotificationConfigCodec` só tem `Encode`.
Varredura textual completa por `pushNotificationConfig` em `apps/api`,
`apps/workers`, `apps/inbox` e `tests/` (raiz) não encontrou nenhum
código de produção que leia essa chave de volta como `PushNotificationConfig`
tipado — só o `Encode` em `AgentExecutionService.BuildTerminalMetadata`
grava, e só `PushNotificationEndToEndTests.cs` lê, como `JsonElement`
bruto via `GetProperty`. Diferente de `DelegationDepth`, que tem `Decode`
e um consumidor real (`AgentDelegationToolSetResolver`), não há
consumidor interno de `pushNotificationConfig` a proteger.

Ainda assim, verificado por reflection real que um `Decode` hipotético
usando `A2AJsonUtilities.DefaultOptions` toleraria linhas antigas: essas
opções herdam `JsonSerializerDefaults.Web`, que fixa
`PropertyNameCaseInsensitive = true`. Um decode desses lendo
`{"Id":null,"Url":"...",...}` (formato antigo) recupera `Url` normalmente.
Não há gatilho de migração por quebra de leitura interna.

### D3 — "Nenhum consumidor" é conclusão sobre este repo, com a limitação escrita

A varredura (D2) não alcança clientes externos: quem passa
`pushNotificationConfig` num `SendMessage` é, por definição, capaz de
falar o protocolo A2A, e uma integração ativa poderia viver inteiramente
fora deste repo. Verificado que `GET /agents/{id}/a2a` **não** está na
allowlist de rotas anônimas (`Program.cs:78-88`,
`ValidateRouteAuthenticationClassification`) — é autenticada, o que
reduz mas não elimina a superfície de um cliente externo autenticado
consumindo `GetTask`/`ListTasks`.

Dentro do repo, o único cliente A2A conhecido é `apps/inbox`
(`A2AClientFactory` + `DebounceSweepService.SendMessageAsync`), que
nunca chama `GetTaskAsync`/`ListTasksAsync` — só `SendMessageAsync`. Não
é consumidor desta chave.

**Conclusão desta change**: nenhum consumidor de
`Metadata["pushNotificationConfig"]` foi encontrado no código deste
repo. Não há evidência de consumidor externo, mas também não há como
descartá-la por leitura de código — essa é uma limitação do método, não
uma alegação além dele.

### D4 — Sem migração das 37 linhas de dev (nem das de produção), decisão com gatilho

Nada quebra: não existe `Decode` interno (D2), e um leitor futuro que
use `A2AJsonUtilities.DefaultOptions` seria case-insensitive e toleraria
as duas formas. Migrar as linhas exigiria um script de backfill sobre
`a2a_tasks.payload` (campo `jsonb`) só para corrigir um subtree que hoje
não tem consumidor interno confirmado — custo desproporcional ao
benefício conhecido (convenção 2: sem trabalho especulativo sem
consumidor real).

**Consequência aceita, não uma ausência silenciosa de trabalho**: o
banco fica com dois formatos de `pushNotificationConfig` convivendo
indefinidamente em `a2a_tasks` — sem job de limpeza/retenção nessa
tabela (confirmado por varredura), as linhas antigas permanecem para
sempre. Um cliente A2A externo que leia `GetTask` de uma task terminal
gravada antes desta correção recebe `Url`/`Token` PascalCase com nulls
para sempre, mesmo depois do deploy desta change.

**Gatilho para revisitar**: se aparecer um consumidor real (interno ou
externo, ver D3) que precise ler tasks terminais antigas com
`pushNotificationConfig`, avaliar backfill nesse momento — não antes.

### D5 — Asserção negativa no teste E2E (convenção 13)

Além de corrigir `pushConfigElement.GetProperty("Url")` →
`GetProperty("url")` nas duas ocorrências
(`PushNotificationEndToEndTests.cs:67` e `:117`), a asserção ganha a
checagem negativa: afirmar a ausência da chave `Url` (PascalCase) e,
quando aplicável, a ausência de campos opcionais ausentes serializados
como propriedade com valor `null` (`Id`, `Authentication` no cenário de
teste, que só define `Url`). A mesma dupla positiva/negativa se estende
aos dois testes que já variam `Authentication`/`Token`
(`AuthenticationPresent_ResultsInAuthorizationHeader`,
`TokenPresent_ResultsInNotificationTokenHeader`), cobrindo também o
aninhamento `authentication.scheme`/`authentication.credentials` — a
parte do formato mais propensa a regredir, porque depende da naming
policy se aplicar em profundidade, não só no nível raiz do objeto.

Razão para a asserção **negativa dos nulls** (`Id`/`Authentication`/`Token`
ausentes não aparecerem como propriedade com valor `null`): uma correção
parcial futura — alguém arruma a `PropertyNamingPolicy` mas esquece de
configurar `DefaultIgnoreCondition`, ou vice-versa — produziria
`{"id":null,"url":"...","authentication":null,"token":null}`. Isso
**passaria despercebido pela asserção positiva** (`GetProperty("url")`
teria sucesso normalmente, o campo certo está lá) e só a asserção
negativa dos nulls pega essa metade da regressão.

A asserção negativa do casing antigo (`Url` ausente) não carrega o mesmo
peso — vale registrar por que ela não é redundante por acidente, mas
também não é a que fecha o buraco real: `JsonElement.GetProperty` é
case-sensitive (ordinal), diferente de `PropertyNameCaseInsensitive` do
STJ (que vale só para desserialização em POCO). Se o casing regredisse
para `Url`, a própria asserção positiva (`GetProperty("url")`) já
lançaria e o teste já falharia sozinho — a negativa do `Url` é
confirmação redundante desse caso, não a que cobre a lacuna. A negativa
que de fato cobre um caminho que a positiva sozinha não pegaria é a dos
nulls, descrita acima. Mesmo mecanismo do comentário em
`AssertTasksMatch` (`crossapp-session-codec-encoder`, Risco 3): existe
para impedir o "conserto" de volta ao ver um teste vermelho no futuro,
com comentário apontando para esta change.

### D6 — Registro em `01-ARQUITETURA_E_CONVENCOES.md`: o mecanismo do `JsonElement`, não um exemplo novo da convenção 12

Avaliado: a convenção 12 já tem exemplo suficiente sobre "opções de
serialização fazem parte do formato de fio" (registrado por
`crossapp-session-codec-encoder`) — não precisa de um quarto exemplo
repetindo o mesmo ponto com um codec diferente.

O que **não** está registrado em lugar nenhum, e é conhecimento
arquitetural que vale além desta change, é o mecanismo específico que
explica por que dois codecs com o mesmo defeito de forma tiveram
consequências diferentes: `JsonSerializer.Serialize` sobre um
`JsonElement` já materializado não reaplica naming policy nem
`DefaultIgnoreCondition` — só objetos .NET serializados a fresco passam
por essa etapa. Isso significa que a re-serialização de
`SaveTaskAsync`/qualquer `PostgresTaskStore` **não é uma rede de
segurança genérica** para qualquer coisa colocada em `AgentTask.Metadata`
como `JsonElement` — só corrige o que está fora do padrão se o valor
errado nunca foi materializado como `JsonElement` (caso
`ConversationSessionCodec`, que serializa uma string escalar antes do
bug), não se já foi (caso deste `PushNotificationConfigCodec`, que
serializa um objeto).

`tasks.md` inclui uma edição em `01-ARQUITETURA_E_CONVENCOES.md`
(seção "AgentCard / protocolo A2A") registrando esse mecanismo em uma
frase, para que ninguém assuma no futuro que gravar um `JsonElement`
fora do contrato em `Metadata` é seguro "porque `SaveTaskAsync`
normaliza".

## Risks / Trade-offs

- **[Risco] Cliente A2A externo dependendo do formato PascalCase atual**
  → **Não mitigável de dentro do repo** (D3). Justificativa escrita: o
  formato atual viola a spec A2A confirmada na fonte, então qualquer
  cliente conforme à spec já estaria incapaz de ler `url`/`token`
  corretamente hoje; um cliente que dependesse do PascalCase estaria
  acoplado a um defeito, não a um contrato. Registrado como limitação
  reconhecida, não como risco coberto por teste — não há como testar a
  ausência de um consumidor que pode existir fora do repo.
- **[Risco] Correção parcial futura (casing corrigido, nulls não, ou
  vice-versa) reintroduzindo parte do defeito** → **Mitigação**: as
  asserções de D5, que cobrem as duas metades da correção
  independentemente (positiva de casing + negativa de nulls) — inclusive
  no aninhamento `authentication.scheme`/`authentication.credentials`,
  não só nos campos de primeiro nível.
- **[Risco] Linhas antigas em formato divergente convivendo
  indefinidamente em `a2a_tasks`** → **Mitigação**: D4, decisão
  explícita com gatilho registrado (consumidor real de tasks antigas) e
  a consequência escrita, não uma omissão silenciosa.
- **[Risco] Outro site do repo gravando ou lendo essa chave, não
  coberto por esta correção** → **Mitigação**: varredura textual
  completa por `pushNotificationConfig` em `apps/api`, `apps/workers`,
  `apps/inbox` e `tests/` (raiz), resultado registrado em D2 — só o
  `Encode`/teste E2E já conhecidos.
- **[Risco] "Nenhum consumidor" ser lido como "não há consumidor" e
  usado para justificar decisões futuras além do que a varredura
  provou** → **Mitigação**: D3 registra a distinção explicitamente, e
  `tasks.md`/`02-HISTORICO_E_STATUS.md` repetem a frase com a mesma
  precisão ("nenhum consumidor encontrado no código do repo").

## Migration Plan

Sem passos de deploy ou rollback além do deploy normal de
`apps/workers` — correção de biblioteca interna sem mudança de schema,
sem migração de banco, sem coordenação com `apps/api`. Rollback é
reverter o commit; nenhuma linha existente de `a2a_tasks` fica pior do
que já está (D4) — a correção só passa a valer para tasks terminais
novas, gravadas após o deploy.

## Open Questions

Nenhuma incerteza real de negócio/produto em aberto. A decisão de não
migrar dados existentes (D4) e a limitação sobre consumidores externos
(D3) já estão registradas como decisões conscientes, com gatilho, não
como perguntas pendentes.
