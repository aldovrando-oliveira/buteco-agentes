## Context

A etapa 1 (`apps-workers-contexto-temporal`, arquivada) entregou
`TemporalContextBlockBuilder.Build(timeProvider, messageInstant)` com
`messageInstant` sempre `null` — nenhum chamador tinha como fornecê-lo.
Uma rodada de `/opsx:explore` (`inbox-contexto-canal-metadata`) investigou
o transporte com evidência verificada por decompilação do pacote `A2A`
(`1.0.0-preview2`) e rastreamento real do pipeline `apps/inbox` →
`apps/api` → `apps/workers`. Os achados dessa exploração (ver
`proposal.md`) são tratados aqui como fechados, não reinvestigados.

Três apps participam, isolados por convenção (sem `ProjectReference`
cruzado): `apps/inbox` (origem do instante — recebe webhook, bufferiza,
dispara `SendMessage`), `apps/api` (persiste e enfileira, sem mudança de
código nesta change), `apps/workers` (lê, resolve a precedência, e
propaga para tasks delegadas).

## Goals / Non-Goals

**Goals:**
- Transportar o instante de recebimento da última mensagem do buffer de
  debounce de `apps/inbox` até o bloco de contexto temporal montado em
  `apps/workers`.
- Propagar esse instante para tasks criadas por delegação entre agentes,
  para que Source e Target resolvam expressões relativas contra o mesmo
  instante.
- Fazer isso sem exigir nenhuma mudança de código em `apps/api` e sem
  campo novo em `TaskJobMessage` (achados fechados da exploração).
- Tratar ausência do instante (metadata ausente, chave ausente, valor
  ilegível) como caminho normal — nunca falha de task.

**Non-Goals:**
- Contexto de canal (`DisplayName`, `ExternalId`, quinto contrato de
  plugin) — change própria subsequente (`inbox-contexto-canal`), por
  perfil de risco oposto (injeção de prompt via texto livre do usuário
  final, sem mitigação que elimine o risco).
- Alterar o que a etapa 1 fixou: o bloco de contexto temporal em si, a
  regra de precedência em linguagem natural, o ponto único de montagem
  (`AgentExecutionService`), fuso e cultura do worker, o limiar de
  defasagem.
- Recuperar o instante declarado pelo provedor (WAHA/Telegram) em vez do
  instante de recebimento do webhook — `Message.OccurredAt` em
  `apps/inbox` é sempre o instante de recebimento, e nenhum dos dois
  adapters desserializa o timestamp que o provedor envia. Ver "Riscos" e
  "Registro" abaixo — decisão consciente, não lacuna.
- Migrar `WahaInboundWebhookHandler`/`TelegramInboundWebhookHandler` para
  `TimeProvider` — item em aberto registrado, fora do escopo desta
  change.
- Timestamp por mensagem individual dentro do buffer de debounce.
- Contexto como argumento estruturado de tool call MCP.

## Decisions

### D1 — Transporte via `A2A.Message.Metadata`, não via `TaskJobMessage`

`Message.Metadata` (`Dictionary<string, JsonElement>?`) já sobrevive até
`a2a_tasks.payload` sem nenhuma mudança em `apps/api`:
`EnqueueingAgentHandler.ExecuteAsync` enfileira o `Message` inteiro para
o SDK, que o anexa ao `History` do `AgentTask` e persiste via
`PostgresTaskStore.SaveTaskAsync` — serialização **fresca** do
`AgentTask` inteiro com `A2AJsonUtilities.DefaultOptions`, sem risco de
formato aqui porque o `Message` nunca foi pré-materializado como
`JsonElement` antes dessa serialização. Do lado da leitura,
`AgentExecutionService.GetTaskWithRetryAsync` já relê a `AgentTask`
inteira, e a extração da última mensagem do usuário (hoje
`ExtractLatestUserText`) já segura o `Message` completo em mãos.

**Alternativa considerada e descartada**: campo novo em `TaskJobMessage`
(envelope RabbitMQ interno, fora do contrato A2A). Descartada porque
exigiria mudar os dois espelhos do record (`apps/api` e `apps/workers`)
para transportar um valor que o worker já teria acesso de outra forma —
custo sem benefício. `Message.Metadata` é aditivo ao contrato de
protocolo: um cliente A2A externo que não enviar a chave simplesmente
colapsa para o comportamento da etapa 1.

### D2 — Chave `messageInstant`, valor ESCALAR (string ISO 8601 com offset)

Nome da chave: `messageInstant`, seguindo o estilo já usado por outras
chaves de `Metadata` neste pipeline (`conversationSession`,
`pushNotificationConfig`). Formato do valor: string ISO 8601 com offset
(ex. `"2026-08-21T23:58:00-03:00"`), o mesmo formato já usado pelo
instante de processamento no bloco de contexto temporal da etapa 1 —
consistência de formato entre os dois instantes que o LLM recebe.

A exploração deixou claro que o risco do mecanismo "`JsonElement`
sobrevive à re-serialização sem reaplicar naming policy"
(`push-notification-config-codec-encoder`, D6) é condicional ao shape do
valor: **escalar** sem `A2AJsonUtilities.DefaultOptions` é benigno
(classe do `ConversationSessionCodec` — o achado não-crítico); **objeto**
sem essas opções é a variante séria que grava errado no disco (classe do
`PushNotificationConfigCodec`). Escolher um valor escalar remove a
variante séria por construção — não há chave aninhada, não há
casing/nulls de propriedade em risco.

Mesmo assim, e isto não é opcional: o ponto de escrita
(`DebounceSweepService.BuildSendMessageRequest`, em `apps/inbox`, e
`CreateDelegatedTaskAsync`, em `apps/workers` — ver D3) monta o
`JsonElement` do valor com `JsonSerializer.SerializeToElement(value,
A2AJsonUtilities.DefaultOptions)`, nunca com as opções padrão do
`JsonSerializer`. Não é hipotético — é o mesmo padrão de bug já corrigido
duas vezes nesta linha de trabalho, e a disciplina de sempre passar essas
opções explicitamente é o que evita reabri-lo.

**Decisão sobre registrar isto na convenção 12**: avaliado e descartado.
A convenção 12 de `01-ARQUITETURA_E_CONVENCOES.md` já tem três entradas
(enum como string; opções de serialização em geral — encoder, naming
policy, tratamento de null; e o caso concreto de
`crossapp-session-codec-encoder`), e o mecanismo específico de
`JsonElement` sobrevivendo à re-serialização sem reaplicar naming policy
já está documentado na seção "AgentCard / protocolo A2A" do mesmo
arquivo, escrito por `push-notification-config-codec-encoder`. A escolha
de D2 (valor escalar) é uma **aplicação** desse conhecimento já
registrado, não uma lição nova extraída de um defeito novo — os três
exemplos existentes de convenção 12 são todos defeitos encontrados com
lição a tirar; esta change não encontra um defeito, evita um por desenho.
Empilhar um quarto exemplo positivo ao lado de três exemplos de defeito
tornaria a convenção mais longa sem torná-la mais legível. Nenhuma
mudança em `01-ARQUITETURA_E_CONVENCOES.md` é necessária por esta
change — decisão escrita aqui, não tarefa em `tasks.md`.

Como esse helper de escrita é pequeno (uma linha de
`JsonSerializer.SerializeToElement`) e os dois pontos de escrita vivem em
apps isolados sem `ProjectReference` cruzado (`apps/inbox` e
`apps/workers`), ele é **duplicado** em cada app — mesma justificativa já
usada para `ITokenService` (convenção 2: sem abstração prematura; o custo
de duplicar 3-5 linhas é menor que o de justificar uma `libs/` nova para
isso).

### D3 — Propagar o instante da mensagem para tasks delegadas, via a mesma chave `Message.Metadata`

Sem propagação, o Source resolve "amanhã" contra o instante da mensagem
original e o Target — cuja task é processada em momento de relógio
genuinamente posterior, confirmado pela exploração (`DelegateToTargetAsync`
publica um `TaskJobMessage` novo na fila `agent-tasks`; Source faz
polling via `WaitForTerminalStateAsync`; delegação é assíncrona pela
mesma fila, não síncrona em processo) — resolveria contra seu próprio
instante de processamento, posterior ao do Source. Dois agentes na mesma
conversa chegando a datas diferentes para a mesma palavra do cliente é
exatamente a falha que esta linha de trabalho existe para evitar. A
etapa 1 já declarou isso Non-Goal uma vez, com gatilho apontando para
esta change — não adiar de novo sem decisão humana.

**Mecanismo escolhido**: em vez de um segundo caminho de transporte
específico para delegação (campo novo em `TaskJobMessage`, ou passar o
valor "por fora" até o ponto de montagem do bloco), o Target lê o
instante da mensagem exatamente do mesmo jeito que qualquer task lê —
pela chave `Message.Metadata["messageInstant"]` da sua **própria**
`AgentTask`. Isso significa que `CreateDelegatedTaskAsync` precisa
escrever essa chave no `Message` inicial que constrói para o Target,
usando o mesmo valor que o Source extraiu da sua própria task, serializado
com o mesmo cuidado de D2. Um único mecanismo de leitura
(`AgentExecutionService`, para toda task, delegada ou não) em vez de dois.

**Correção de custo sobre o registrado na exploração**: o achado
registrou "3 métodos + 1 assinatura de interface", concluindo que
`CreateDelegatedTaskAsync` não precisaria ser tocado porque "só monta
`AgentTask`/`Metadata`, não o bloco temporal". Essa leitura estava certa
sobre onde `TemporalContextBlockBuilder.Build` é chamado (nunca dentro de
`CreateDelegatedTaskAsync`), mas não sobre o custo total: se o mecanismo
de leitura do Target é o mesmo da etapa 1 (ler a própria `Metadata`),
`CreateDelegatedTaskAsync` **precisa** escrever essa chave no `Message`
que constrói — senão o Target nunca a encontra. Custo real: **4 métodos +
1 assinatura de interface**:
- `IAgentDelegationToolSetResolver.ResolveAsync` — assinatura ganha um
  parâmetro `DateTimeOffset? messageInstant`.
- `AgentExecutionService.ExecuteAsync` (chamador de `ResolveAsync`,
  linhas ~169-170) — passa o `messageInstant` já extraído da própria task
  nesse ponto (o mesmo valor usado na chamada a
  `TemporalContextBlockBuilder.Build` na linha ~178).
- `BuildDelegationTool` — a closure captura `messageInstant` junto com
  `contextId`/`currentDepth`, hoje já capturados.
- `DelegateToTargetAsync` — repassa `messageInstant` para
  `CreateDelegatedTaskAsync`.
- `CreateDelegatedTaskAsync` — grava `messageInstant` em
  `Message.Metadata["messageInstant"]` do `Message` que constrói para o
  Target, com o mesmo helper de serialização de D2.

Este parágrafo corrige o registro em `02-HISTORICO_E_STATUS.md` (ver
`tasks.md`, seção de Registro) — o custo estava subestimado tanto no
número de pontos quanto no motivo de `CreateDelegatedTaskAsync` estar de
fora.

**Invariante do mecanismo, nomeado e verificado contra o código real
(convenção 6, não assumido)**: o mecanismo só funciona se o `Message`
que `CreateDelegatedTaskAsync` constrói para o Target for **encontrável**
pelo mesmo filtro que a extração de mensagem de usuário aplica — se não
casar, a Tarefa 3.6 (que só confirma que a chave foi escrita) passaria
mesmo assim, e o mecanismo falharia em silêncio, sem nenhum teste
pegando.

Verificado: `ExtractLatestUserText`
(`AgentExecutionService.cs:323-327`) filtra só por `m.Role == Role.User`
sobre `task.History`, pegando o último (`LastOrDefault`) — sem nenhum
outro critério de exclusão (não filtra por `TaskId`, `ContextId`, nem
posição). O `Message` que `CreateDelegatedTaskAsync` constrói
(`AgentDelegationToolSetResolver.cs:147-155`) tem `Role = Role.User`, e
entra no `History` da task delegada pelo mesmo mecanismo de projeção
(`TaskProjection.Apply`) usado por `apps/api` — não por atribuição direta
de "mensagem inicial". Como é a única mensagem de usuário na task recém-
criada, ela é ao mesmo tempo a única e a última `Role.User` do histórico
— satisfaz o filtro de `ExtractLatestUserText` sem ambiguidade. O
invariante se sustenta: nenhuma mudança de filtro seria necessária para
o Target encontrar sua própria `messageInstant`.

Esta verificação não substitui a Tarefa 3.8 do `tasks.md` — ela prova a
escrita da chave (Tarefa 3.6) e a leitura correta pelo mecanismo
existente (este parágrafo), mas só um teste de ponta a ponta com Source e
Target processados em instantes de processamento diferentes (Tarefa 3.8)
prova que a propagação funciona sob a condição real que D3 existe para
resolver — a Tarefa 3.8 é obrigatória, não alternativa à 3.6.

### D4 — Ausência é caminho normal, tratado num único ponto

Nem todo `SendMessage` vem de `apps/inbox` (um cliente A2A externo pode
não enviar a chave) e nem toda task é de primeiro nível (delegação
propaga via D3, mas ainda depende de o Source ter tido um instante para
propagar). Três casos, todos tratados como "sem instante de mensagem",
nunca como erro de task:
1. `Message.Metadata` é `null` — sem instante.
2. `Message.Metadata` não é `null`, mas não contém a chave
   `messageInstant` — sem instante.
3. A chave existe, mas o valor não é uma string ISO 8601 parseável —
   tratado como sem instante, e um log de nível `Warning` é emitido (não
   `Error`) — porque não impede a task de progredir, mas um valor
   presente e ilegível é sinal de um bug em algum produtor da chave, e
   merece visibilidade diferente de "simplesmente não veio".

Nos três casos, o comportamento resultante é idêntico ao da etapa 1: a
regra de precedência resolve contra o instante de processamento. A
extração (tentativa de leitura + parse) vive num único método privado em
`AgentExecutionService`, chamado tanto para o caminho direto quanto,
indiretamente, para o caminho de delegação (D3) — o Target não tem um
caminho de extração separado, usa o mesmo.

## Riscos / Trade-offs

- **[Risco] Valor gravado fora do contrato de serialização** (mesma
  classe de `PushNotificationConfigCodec`) → **Mitigação**: D2 (valor
  escalar, elimina a variante séria por construção) + disciplina de
  sempre passar `A2AJsonUtilities.DefaultOptions` nos dois pontos de
  escrita + teste de acordo real (convenção 11) afirmando o JSON bruto
  persistido, não o objeto intermediário — ver `tasks.md`.
- **[Risco] Metadata ausente ou ilegível quebra a task** → **Mitigação**:
  D4, os três casos tratados como ausência e testados em par
  "com"/"sem" (convenção 5).
- **[Risco] Source e Target divergindo na resolução de "amanhã"** →
  **Mitigação**: D3 (propagação) + teste específico com Source e Target
  processados em instantes de processamento diferentes, ambos resolvendo
  contra o mesmo `messageInstant` — sem esse teste, a propagação entra
  sem contraparte (convenção 10).
- **[Risco] Âncora já defasada por atraso do lado do provedor** (WAHA
  fora do ar, reentrega tardia) → **Não mitigável nesta change**:
  `Message.OccurredAt` em `apps/inbox` é sempre o instante de
  recebimento do webhook, nunca o declarado pelo provedor — confirmado
  nos dois adapters, que chamam `DateTimeOffset.UtcNow` inline e sequer
  desserializam o timestamp que WAHA e Telegram enviam. Registrado como
  item em aberto (ver `tasks.md`, Registro), não coberto por teste desta
  change — justificativa escrita, conforme convenção 10.
- **[Risco] Virada de dia dentro da janela do buffer desloca a data
  resolvida** → **Não mitigável nesta change, risco pequeno por
  construção**: a âncora escolhida é a última mensagem do buffer
  (decidida na etapa 1, timestamp por mensagem individual é Non-Goal),
  não a mensagem que efetivamente continha a expressão relativa. Se o
  cliente escrever "tem lugar amanhã?" às 23:58 e complementar "de
  manhã, se der" às 00:03, a âncora vira 00:03 e "amanhã" desloca um
  dia. A aritmética não acontece em `apps/inbox` — nenhuma acontece ali,
  o valor passa opaco — acontece no LLM, em `apps/workers`, contra a
  âncora que `apps/inbox` escolheu. Risco pequeno com a janela de
  debounce atual (segundos), crescendo se a janela virar configurável
  para minutos. Gatilho: revisitar a decisão de âncora se a janela de
  debounce deixar de ser da ordem de segundos.
- **[Risco] Fixture de teste forjado nas duas pontas mascara um formato
  de fio errado** (já aconteceu duas vezes nesta base — chave de
  assinatura de token, enums de `Message`) → **Mitigação**: o teste de
  acordo do item acima usa o valor real produzido por `apps/inbox`
  consumido por `apps/workers`, lendo JSON bruto da resposta/registro
  persistido, não round-trip pelo mesmo tipo C#.

## Migration Plan

Nenhuma migração de banco — `PendingDispatch.LastMessageAt` já existe
(coluna simples, fora do owned/JSON). Mudança de protocolo é aditiva:
`Message.Metadata["messageInstant"]` é um campo novo e opcional dentro de
um dicionário já existente no wire shape do A2A — nenhum cliente A2A
existente quebra por não enviá-lo, e nenhum dado persistido precisa de
backfill. Sem fases de deploy — os três apps podem subir na ordem normal.

## Open Questions

Nenhuma pergunta aberta de produto/negócio identificada — a exploração
prévia e as Decisions acima fecham as escolhas técnicas. Os itens que
seguem em aberto (recuperar o instante declarado pelo provedor, migrar os
adapters para `TimeProvider`) são Non-Goals conscientes desta change, não
incertezas a decidir aqui — registrados em `tasks.md`.

## Árvore de arquivos tocados (todos já existentes — nenhum arquivo/pasta novo)

```
apps/inbox/src/Buteco.Inbox/
└── Orchestration/
    └── DebounceSweepService.cs          # BuildSendMessageRequest: seta Message.Metadata["messageInstant"]

apps/inbox/tests/Buteco.Inbox.Tests/
└── Orchestration/
    └── DebounceSweepServiceTests.cs     # (ou arquivo equivalente já existente) novo(s) caso(s) de teste

apps/workers/src/Buteco.Workers/
├── Agents/
│   └── AgentExecutionService.cs         # extração de messageInstant, repasse ao TemporalContextBlockBuilder.Build
└── AgentDelegations/
    ├── AgentDelegationToolSetResolver.cs      # ResolveAsync, BuildDelegationTool, DelegateToTargetAsync, CreateDelegatedTaskAsync
    └── IAgentDelegationToolSetResolver.cs     # assinatura de ResolveAsync ganha parâmetro messageInstant

apps/workers/tests/Buteco.Workers.Tests/
├── Agents/
│   └── AgentExecutionServiceTests.cs    # (ou arquivo equivalente já existente) novo(s) caso(s) de teste
└── AgentDelegations/
    └── AgentDelegationToolSetResolverTests.cs   # teste de divergência Source/Target (D3)
```

Sem mudança em `apps/api` e sem mudança em `apps/frontend` nesta change
— portanto sem testes novos nesses dois apps.
